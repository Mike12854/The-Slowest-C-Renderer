using System;
using API.Commands.Components;
using API.Commands.Enums;
using API.Enums;
using Silk.NET.Vulkan;
using Vulkan.Core;
using Vulkan.Resource;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Vulkan.Execution;

/// <summary>
/// Übersetzt einen vorsortierten <see cref="StRenderCommand"/>-Slice direkt per Pointer-Iteration
/// in native Vulkan-1.3-Befehle auf einem Secondary-<see cref="CommandBuffer"/>.
/// Nutzt Lazy-Descriptor-Flushing (<c>descriptorsDirty</c>), sodass DescriptorSets erst unmittelbar
/// vor einem Draw-Call gebunden werden, wenn alle Slots (CBV, SRV, Texturen) gesetzt sind.
/// </summary>
internal sealed unsafe class VulkanCommandExecutor
{
    /// <summary>
    /// Zeichnet alle Befehle aus <paramref name="commands"/> auf den Worker-eigenen <paramref name="cmd"/> auf.
    /// Da jeder Worker seinen eigenen Secondary CommandBuffer besitzt, werden Viewport und Scissor
    /// initial pro Slice gesetzt und der lokale Binding-State sauber getrackt.
    /// </summary>
    public void RecordSlice(
        VulkanContext context,
        CommandBuffer cmd,
        Extent2D renderExtent,
        VulkanResourcePool<VulkanPipeline> pipelines,
        VulkanResourcePool<VulkanBuffer> buffers,
        ReadOnlySpan<StRenderCommand> commands,
        VulkanResourcePool<VulkanMesh> meshes,
        VulkanResourcePool<VulkanTexture> textures)
    {
        if (commands.IsEmpty)
            return;

        var vk = context.Api;

        // Dynamic Viewport & Scissor (Vulkan 1.3 Dynamic State) pro Secondary CommandBuffer setzen
        var viewport = new Viewport(0, 0, renderExtent.Width, renderExtent.Height, 0.0f, 1.0f);
        var scissor = new Rect2D(new Offset2D(0, 0), renderExtent);
        vk.CmdSetViewport(cmd, 0, 1, &viewport);
        vk.CmdSetScissor(cmd, 0, 1, &scissor);

        VulkanPipeline? currentPipeline = null;
        var boundCBuffers = new VulkanBuffer?[8];
        var boundSBuffers = new VulkanBuffer?[8];
        var boundTextures = new VulkanTexture?[8];
        bool descriptorsDirty = false;

        fixed (StRenderCommand* pCmds = commands)
        {
            for (int i = 0; i < commands.Length; i++)
            {
                StRenderCommand* command = &pCmds[i];

                switch (command->Type)
                {
                    case ERenderCommandType.NoOp:
                        break;

                    case ERenderCommandType.SetPipeline:
                    {
                        var pipeline = pipelines.Get(command->Pipeline.Index, command->Pipeline.Generation);
                        if (!ReferenceEquals(pipeline, currentPipeline))
                        {
                            currentPipeline = pipeline;
                            vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline.Handle);
                            descriptorsDirty = pipeline.HasDescriptors;
                        }
                        break;
                    }

                    case ERenderCommandType.SetVertexBuffer:
                    {
                        var buffer = buffers.Get(command->Buffer.Index, command->Buffer.Generation);
                        var vkBuf = buffer.Handle;
                        ulong offset = 0;
                        vk.CmdBindVertexBuffers(cmd, command->Slot, 1, &vkBuf, &offset);
                        break;
                    }

                    case ERenderCommandType.SetIndexBuffer:
                    {
                        var buffer = buffers.Get(command->Buffer.Index, command->Buffer.Generation);
                        vk.CmdBindIndexBuffer(cmd, buffer.Handle, 0, IndexType.Uint32);
                        break;
                    }

                    case ERenderCommandType.SetConstantBuffer:
                    {
                        if (command->Slot < 8)
                        {
                            boundCBuffers[command->Slot] = buffers.Get(command->Buffer.Index, command->Buffer.Generation);
                            descriptorsDirty = true;
                        }
                        break;
                    }

                    case ERenderCommandType.SetStructuredBuffer:
                    {
                        if (command->Slot < 8)
                        {
                            boundSBuffers[command->Slot] = buffers.Get(command->Buffer.Index, command->Buffer.Generation);
                            descriptorsDirty = true;
                        }
                        break;
                    }

                    case ERenderCommandType.SetTexture:
                    {
                        if (command->Slot < 8)
                        {
                            boundTextures[command->Slot] = textures.Get(command->Texture.Index, command->Texture.Generation);
                            descriptorsDirty = true;
                        }
                        break;
                    }

                    case ERenderCommandType.SetPushConstants:
                    {
                        if (currentPipeline != null && command->PushDataSize > 0)
                        {
                            vk.CmdPushConstants(
                                cmd,
                                currentPipeline.Layout,
                                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                                command->PushDataOffset,
                                command->PushDataSize,
                                command->PushData);
                        }
                        break;
                    }

                    case ERenderCommandType.Draw:
                    {
                        FlushDescriptors(vk, cmd, currentPipeline, boundCBuffers, boundSBuffers, boundTextures, ref descriptorsDirty);
                        vk.CmdDraw(cmd, command->VertexCount, Math.Max(1u, command->InstanceCount), 0, 0);
                        break;
                    }

                    case ERenderCommandType.DrawIndexed:
                    {
                        FlushDescriptors(vk, cmd, currentPipeline, boundCBuffers, boundSBuffers, boundTextures, ref descriptorsDirty);
                        vk.CmdDrawIndexed(
                            cmd,
                            command->IndexCount,
                            Math.Max(1u, command->InstanceCount),
                            command->StartIndex,
                            command->BaseVertex,
                            command->StartInstance);
                        break;
                    }

                    case ERenderCommandType.DrawMesh:
                    {
                        FlushDescriptors(vk, cmd, currentPipeline, boundCBuffers, boundSBuffers, boundTextures, ref descriptorsDirty);
                        var mesh = meshes.Get(command->Mesh.Index, command->Mesh.Generation);
                        var vb = mesh.VertexBuffer.Handle;
                        ulong offset = 0;
                        vk.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);

                        if (mesh.IndexBuffer != null && mesh.IndexCount > 0)
                        {
                            var idxType = mesh.GetIndexFormat() == EIndexFormat.UInt16 ? IndexType.Uint16 : IndexType.Uint32;
                            vk.CmdBindIndexBuffer(cmd, mesh.IndexBuffer.Handle, 0, idxType);
                            vk.CmdDrawIndexed(
                                cmd, 
                                mesh.IndexCount, 
                                Math.Max(1u, command->InstanceCount), 
                                0, 
                                0, 
                                command->StartInstance);
                        }
                        break;
                    }
                }
            }
        }
    }

    private static void FlushDescriptors(
        Vk vk,
        CommandBuffer cmd,
        VulkanPipeline? pipeline,
        VulkanBuffer?[] cbuffers,
        VulkanBuffer?[] sbuffers,
        VulkanTexture?[] textures,
        ref bool dirty)
    {
        if (!dirty || pipeline == null || !pipeline.HasDescriptors)
            return;

        var set = pipeline.GetOrCreateDescriptorSet(cbuffers, sbuffers, textures);
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, pipeline.Layout, 0, 1, &set, 0, null);
        dirty = false;
    }
}