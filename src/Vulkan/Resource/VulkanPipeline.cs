using System;
using System.Collections.Concurrent;
using API.Commands.Enums;
using API.Descriptors.Components;
using API.Descriptors.Enums;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Vulkan.Core;
using Vulkan.Shaders;
using ECullMode = API.Enums.ECullMode;
using EPrimitiveTopology = API.Enums.EPrimitiveTopology;
using EVertexFormat = API.Enums.EVertexFormat;

namespace Vulkan.Resource;

/// <summary>
/// Kapselt ein Vulkan-1.3-Graphics-<see cref="Pipeline"/>-State-Object (PSO) inklusive
/// <see cref="PipelineLayout"/>, <see cref="DescriptorSetLayout"/> und einem thread-sicheren,
/// FNV-1a-gehashten <see cref="DescriptorSet"/>-Cache.
/// Binding-Konvention in Set 0 (HLSL/DXC-kompatibel):
/// <c>[0..CB-1]</c> UniformBuffer -> <c>[CB..CB+SB-1]</c> StorageBuffer -> paarweise <c>SampledImage</c> + <c>Sampler</c>.
/// Verwendet standardmäßig Reversed-Z (<see cref="CompareOp.Greater"/>) und <see cref="FrontFace.Clockwise"/>.
/// </summary>
internal sealed unsafe class VulkanPipeline : IDisposable
{
    private readonly VulkanContext _context;
    private readonly object _descriptorLock = new();
    private readonly ConcurrentDictionary<ulong, DescriptorSet> _descriptorSetCache = new();

    /// <summary>Natives Vulkan-<see cref="Pipeline"/>-Handle.</summary>
    public Pipeline Handle { get; private set; }

    /// <summary>Natives <see cref="PipelineLayout"/> (inkl. DescriptorSet-Layout und PushConstant-Range).</summary>
    public PipelineLayout Layout { get; private set; }

    /// <summary>Natives <see cref="DescriptorSetLayout"/> für Set 0.</summary>
    public DescriptorSetLayout SetLayout { get; private set; }

    private DescriptorPool _descriptorPool;

    /// <summary>Anzahl der konfigurierten Constant-Buffer-Slots (UBO).</summary>
    public uint ConstantBufferCount { get; private set; }

    /// <summary>Anzahl der konfigurierten Structured-Buffer-Slots (SSBO).</summary>
    public uint StructuredBufferCount { get; private set; }

    /// <summary>Anzahl der konfigurierten Textur-Slots (jeweils 1 <c>SampledImage</c>- + 1 <c>Sampler</c>-Binding).</summary>
    public uint TextureCount { get; private set; }

    /// <summary><c>true</c>, wenn diese Pipeline mindestens ein Descriptor-Binding besitzt.</summary>
    public bool HasDescriptors => (ConstantBufferCount + StructuredBufferCount + TextureCount) > 0;

    public VulkanPipeline(VulkanContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Erstellt DescriptorSetLayout, DescriptorPool (max. 512 Sets), PipelineLayout und die
    /// Vulkan-1.3-Graphics-Pipeline (via <see cref="PipelineRenderingCreateInfo"/>) anhand von <paramref name="desc"/>.
    /// </summary>
    public void Initialize(in RStPipelineDesc desc, VulkanShaderModule shader)
    {
        var vk = _context.Api;
        var device = _context.Device;

        ConstantBufferCount = desc.ConstantBufferSlotCount;
        StructuredBufferCount = desc.StructuredBufferSlotCount;
        TextureCount = desc.TextureSlotCount;

        uint totalBindings = ConstantBufferCount + StructuredBufferCount + (TextureCount * 2);
        if (totalBindings > 0)
        {
            var bindings = stackalloc DescriptorSetLayoutBinding[(int)totalBindings];
            uint bIdx = 0;

            for (uint i = 0; i < ConstantBufferCount; i++, bIdx++)
            {
                bindings[bIdx] = new DescriptorSetLayoutBinding
                {
                    Binding = bIdx,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.UniformBuffer,
                    StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
                };
            }

            for (uint i = 0; i < StructuredBufferCount; i++, bIdx++)
            {
                bindings[bIdx] = new DescriptorSetLayoutBinding
                {
                    Binding = bIdx,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.StorageBuffer,
                    StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
                };
            }

            for (uint i = 0; i < TextureCount; i++)
            {
                uint texBinding = bIdx++;
                bindings[texBinding] = new DescriptorSetLayoutBinding
                {
                    Binding = texBinding,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.SampledImage,
                    StageFlags = ShaderStageFlags.FragmentBit
                };

                uint sampBinding = bIdx++;
                bindings[sampBinding] = new DescriptorSetLayoutBinding
                {
                    Binding = sampBinding,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.Sampler,
                    StageFlags = ShaderStageFlags.FragmentBit
                };
            }

            var setLayoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = totalBindings,
                PBindings = bindings
            };
            vk.CreateDescriptorSetLayout(device, &setLayoutInfo, null, out var setLayout);
            SetLayout = setLayout;

            var poolSizes = stackalloc DescriptorPoolSize[4];
            uint poolSizeCount = 0;
            const uint maxSets = 512;

            if (ConstantBufferCount > 0)
                poolSizes[poolSizeCount++] =
                    new DescriptorPoolSize(DescriptorType.UniformBuffer, ConstantBufferCount * maxSets);
            if (StructuredBufferCount > 0)
                poolSizes[poolSizeCount++] =
                    new DescriptorPoolSize(DescriptorType.StorageBuffer, StructuredBufferCount * maxSets);
            if (TextureCount > 0)
            {
                poolSizes[poolSizeCount++] =
                    new DescriptorPoolSize(DescriptorType.SampledImage, TextureCount * maxSets);
                poolSizes[poolSizeCount++] = new DescriptorPoolSize(DescriptorType.Sampler, TextureCount * maxSets);
            }

            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = poolSizeCount,
                PPoolSizes = poolSizes,
                MaxSets = maxSets
            };
            vk.CreateDescriptorPool(device, &poolInfo, null, out _descriptorPool);
        }

        var setLayoutHandle = SetLayout;
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = desc.PushConstantBytes
        };

        bool hasPushConstants = desc.PushConstantBytes > 0;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = HasDescriptors ? 1u : 0u,
            PSetLayouts = HasDescriptors ? &setLayoutHandle : null,
            PushConstantRangeCount = hasPushConstants ? 1u : 0u,
            PPushConstantRanges = hasPushConstants ? &pushConstantRange : null
        };

        if (vk.CreatePipelineLayout(device, &layoutInfo, null, out var pipelineLayout) != Result.Success)
            throw new InvalidOperationException("PipelineLayout konnte nicht erstellt werden.");
        Layout = pipelineLayout;

        var vertEntryPtr = SilkMarshal.StringToPtr(shader.VertexEntryPoint);
        var fragEntryPtr = SilkMarshal.StringToPtr(shader.FragmentEntryPoint);

        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = shader.VertexModule,
                PName = (byte*)vertEntryPtr
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = shader.FragmentModule,
                PName = (byte*)fragEntryPtr
            };

            var bindingDesc = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = desc.VertexStride,
                InputRate = VertexInputRate.Vertex
            };

            var vertexSpan = desc.VertexLayout.Span;
            int attrCount = vertexSpan.Length;
            var attrDescs = stackalloc VertexInputAttributeDescription[Math.Max(1, attrCount)];

            for (int i = 0; i < attrCount; i++)
            {
                var attr = vertexSpan[i];
                attrDescs[i] = new VertexInputAttributeDescription
                {
                    Location = attr.Location,
                    Binding = attr.Binding,
                    Offset = attr.OffsetInBytes,
                    Format = attr.Format switch
                    {
                        EVertexFormat.Float1 => Format.R32Sfloat,
                        EVertexFormat.Float2 => Format.R32G32Sfloat,
                        EVertexFormat.Float3 => Format.R32G32B32Sfloat,
                        EVertexFormat.Float4 => Format.R32G32B32A32Sfloat,
                        EVertexFormat.UInt1 => Format.R32Uint,
                        EVertexFormat.UInt2 => Format.R32G32Uint,
                        EVertexFormat.UInt4 => Format.R32G32B32A32Uint,
                        EVertexFormat.Byte4Norm => Format.R8G8B8A8Unorm,
                        _ => Format.R32G32B32Sfloat
                    }
                };
            }

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = desc.VertexStride > 0 ? 1u : 0u,
                PVertexBindingDescriptions = desc.VertexStride > 0 ? &bindingDesc : null,
                VertexAttributeDescriptionCount = (uint)attrCount,
                PVertexAttributeDescriptions = attrCount > 0 ? attrDescs : null
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = desc.Topology switch
                {
                    EPrimitiveTopology.LineList => PrimitiveTopology.LineList,
                    EPrimitiveTopology.PointList => PrimitiveTopology.PointList,
                    _ => PrimitiveTopology.TriangleList
                },
                PrimitiveRestartEnable = false
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1
            };
            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = desc.CullMode switch
                {
                    ECullMode.Front => CullModeFlags.FrontBit,
                    ECullMode.Back => CullModeFlags.BackBit,
                    _ => CullModeFlags.None
                },
                FrontFace = FrontFace.Clockwise
            };
            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };
            var colorBlendAttachment = desc.BlendMode switch
            {
                EBlendMode.AlphaBlend => new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                     ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = true,
                    SrcColorBlendFactor = BlendFactor.SrcAlpha,
                    DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    ColorBlendOp = BlendOp.Add,
                    SrcAlphaBlendFactor = BlendFactor.One,
                    DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                    AlphaBlendOp = BlendOp.Add
                },
                EBlendMode.Additive => new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                     ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = true,
                    SrcColorBlendFactor = BlendFactor.SrcAlpha,
                    DstColorBlendFactor = BlendFactor.One,
                    ColorBlendOp = BlendOp.Add,
                    SrcAlphaBlendFactor = BlendFactor.One,
                    DstAlphaBlendFactor = BlendFactor.One,
                    AlphaBlendOp = BlendOp.Add
                },
                _ => new PipelineColorBlendAttachmentState // EBlendMode.Opaque
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                     ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = false
                }
            };
            var colorFormat = desc.RenderTargetFormat switch
            {
                ETextureFormat.None => Format.Undefined,
                ETextureFormat.RGBA8_UNorm => Format.R8G8B8A8Unorm,
                ETextureFormat.RGBA8_UNorm_SRgb => Format.R8G8B8A8Srgb,
                ETextureFormat.BGRA8_UNorm => Format.B8G8R8A8Unorm,
                ETextureFormat.RGBA16_Float => Format.R16G16B16A16Sfloat,
                _ => Format.B8G8R8A8Srgb
            };
            bool hasColor = colorFormat != Format.Undefined;
            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = hasColor ? 1u : 0u,
                PAttachments = &colorBlendAttachment
            };
            var dynamicStates = stackalloc DynamicState[2];
            dynamicStates[0] = DynamicState.Viewport;
            dynamicStates[1] = DynamicState.Scissor;
            var dynamicStateInfo = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };
            var depthFormat = desc.DepthFormat switch
            {
                ETextureFormat.D32_Float => Format.D32Sfloat,
                ETextureFormat.D24_UNorm_S8_UInt => Format.D24UnormS8Uint,
                _ => Format.Undefined
            };
            var renderingInfo = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = hasColor ? 1u : 0u,
                PColorAttachmentFormats = hasColor ? &colorFormat : null,
                DepthAttachmentFormat = depthFormat
            };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = desc.DepthTestEnable,
                DepthWriteEnable = desc.DepthWriteEnable,
                DepthCompareOp = CompareOp.Greater, // Reversed-Z Standard!
                DepthBoundsTestEnable = false,
                StencilTestEnable = false
            };
            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &renderingInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicStateInfo,
                Layout = Layout,
                PDepthStencilState = &depthStencil
            };
            if (vk.CreateGraphicsPipelines(device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                throw new InvalidOperationException("GraphicsPipeline konnte nicht erstellt werden.");
            Handle = pipeline;
        }
        finally
        {
            SilkMarshal.Free(vertEntryPtr);
            SilkMarshal.Free(fragEntryPtr);
        }
    }

    /// 
    /// Liefert ein gecachtes  für die exakte Kombination aus Constant-Buffern,
    /// Structured-Buffern und Texturen zurück (lock-freier Fastpath) oder allokiert und beschreibt es
    /// thread-sicher bei einem Cache-Miss.
    /// 
    public DescriptorSet GetOrCreateDescriptorSet(
        ReadOnlySpan<VulkanBuffer?> constantBuffers,
        ReadOnlySpan<VulkanBuffer?> structuredBuffers,
        ReadOnlySpan<VulkanTexture?> textures)
    {
        ulong hash = ComputeBindingHash(constantBuffers, structuredBuffers, textures);
        if (_descriptorSetCache.TryGetValue(hash, out var cached))
            return cached;
        lock (_descriptorLock)
        {
            if (_descriptorSetCache.TryGetValue(hash, out cached))
                return cached;
            var layout = SetLayout;
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout
            };
            _context.Api.AllocateDescriptorSets(_context.Device, &allocInfo, out var newSet);
            int maxWrites = (int)(ConstantBufferCount + StructuredBufferCount + (TextureCount * 2));
            var writes = stackalloc WriteDescriptorSet[Math.Max(1, maxWrites)];
            var bufferInfos =
                stackalloc DescriptorBufferInfo[Math.Max(1, (int)(ConstantBufferCount + StructuredBufferCount))];
            var imageInfos = stackalloc DescriptorImageInfo[Math.Max(1, (int)(TextureCount * 2))];
            uint writeCount = 0;
            uint bufIdx = 0;
            uint imgIdx = 0;
            uint binding = 0;
            for (int i = 0; i < ConstantBufferCount; i++, binding++)
            {
                var buf = i < constantBuffers.Length ? constantBuffers[i] : null;
                if (buf == null) continue;
                bufferInfos[bufIdx] = new DescriptorBufferInfo(buf.Handle, 0, buf.SizeInBytes);
                writes[writeCount++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = newSet,
                    DstBinding = binding,
                    DescriptorType = DescriptorType.UniformBuffer,
                    DescriptorCount = 1,
                    PBufferInfo = &bufferInfos[bufIdx++]
                };
            }

            for (int i = 0; i < StructuredBufferCount; i++, binding++)
            {
                var buf = i < structuredBuffers.Length ? structuredBuffers[i] : null;
                if (buf == null) continue;
                bufferInfos[bufIdx] = new DescriptorBufferInfo(buf.Handle, 0, buf.SizeInBytes);
                writes[writeCount++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = newSet,
                    DstBinding = binding,
                    DescriptorType = DescriptorType.StorageBuffer,
                    DescriptorCount = 1,
                    PBufferInfo = &bufferInfos[bufIdx++]
                };
            }

            for (int i = 0; i < TextureCount; i++)
            {
                var tex = i < textures.Length ? textures[i] : null;
                if (tex == null)
                {
                    binding += 2;
                    continue;
                }

// Write 1: SampledImage (ohne Sampler)
                imageInfos[imgIdx] = new DescriptorImageInfo(default, tex.View, ImageLayout.ShaderReadOnlyOptimal);
                writes[writeCount++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = newSet,
                    DstBinding = binding++,
                    DescriptorType = DescriptorType.SampledImage,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[imgIdx++]
                };
// Write 2: Sampler (ohne ImageView)
                imageInfos[imgIdx] = new DescriptorImageInfo(tex.Sampler, default, ImageLayout.Undefined);
                writes[writeCount++] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = newSet,
                    DstBinding = binding++,
                    DescriptorType = DescriptorType.Sampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[imgIdx++]
                };
            }

            if (writeCount > 0)
                _context.Api.UpdateDescriptorSets(_context.Device, writeCount, writes, 0, null);
            _descriptorSetCache[hash] = newSet;
            return newSet;
        }
    }

// 64-Bit FNV-1a Hash über die aktiven nativen Vulkan-Handles der gebundenen Ressourcen
    private static ulong ComputeBindingHash(
        ReadOnlySpan<VulkanBuffer?> cbuffers,
        ReadOnlySpan<VulkanBuffer?> sbuffers,
        ReadOnlySpan<VulkanTexture?> textures)
    {
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < cbuffers.Length; i++)
            hash = (hash ^ (cbuffers[i]?.Handle.Handle ?? 0UL)) * 1099511628211UL;
        for (int i = 0; i < sbuffers.Length; i++)
            hash = (hash ^ (sbuffers[i]?.Handle.Handle ?? 0UL)) * 1099511628211UL;
        for (int i = 0; i < textures.Length; i++)
            hash = (hash ^ (textures[i]?.Image.Handle ?? 0UL)) * 1099511628211UL;
        return hash;
    }

    /// Zerstört Pipeline, PipelineLayout, DescriptorPool und DescriptorSetLayout deterministisch.
    public void Dispose()
    {
        if (_context.Device.Handle == 0) return;
        var vk = _context.Api;
        if (Handle.Handle != 0) vk.DestroyPipeline(_context.Device, Handle, null);
        if (Layout.Handle != 0) vk.DestroyPipelineLayout(_context.Device, Layout, null);
        if (_descriptorPool.Handle != 0) vk.DestroyDescriptorPool(_context.Device, _descriptorPool, null);
        if (SetLayout.Handle != 0) vk.DestroyDescriptorSetLayout(_context.Device, SetLayout, null);
        _descriptorSetCache.Clear();
    }
}