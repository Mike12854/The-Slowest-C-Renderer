using System;
using Silk.NET.Vulkan;
using Vulkan.Core;

namespace Vulkan.Shaders;

/// <summary>
/// Kapselt das gepaarte Vertex- und Fragment-<see cref="ShaderModule"/> sowie deren Einstiegspunkte
/// und SPIR-V-Bytecode für die Verwendung in einer <see cref="Vulkan.Resource.VulkanPipeline"/>.
/// </summary>
internal sealed unsafe class VulkanShaderModule : IDisposable
{
    private readonly VulkanContext _context;

    /// <summary>Natives Vulkan-<see cref="ShaderModule"/> für die Vertex-Stage.</summary>
    public ShaderModule VertexModule { get; private set; }

    /// <summary>Natives Vulkan-<see cref="ShaderModule"/> für die Fragment-/Pixel-Stage.</summary>
    public ShaderModule FragmentModule { get; private set; }

    /// <summary>Name der Einstiegsfunktion im Vertex-Shader (z. B. <c>"VSMain"</c> oder <c>"main"</c>).</summary>
    public string VertexEntryPoint { get; private set; } = "main";

    /// <summary>Name der Einstiegsfunktion im Fragment-Shader (z. B. <c>"PSMain"</c> oder <c>"main"</c>).</summary>
    public string FragmentEntryPoint { get; private set; } = "main";

    /// <summary>Roh-Bytecode des Vertex-Shaders (32-Bit-SPIR-V-Words).</summary>
    public byte[] SpirvVertexBytecode { get; private set; } = Array.Empty<byte>();

    /// <summary>Roh-Bytecode des Fragment-Shaders (32-Bit-SPIR-V-Words).</summary>
    public byte[] SpirvFragmentBytecode { get; private set; } = Array.Empty<byte>();

    public VulkanShaderModule(VulkanContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Erstellt die nativen Vertex- und Fragment-<see cref="ShaderModule"/>-Handles aus gültigem SPIR-V-Bytecode.
    /// </summary>
    public void Initialize(
        byte[] vertexSpirv,
        byte[] fragmentSpirv,
        string vertexEntryPoint,
        string fragmentEntryPoint)
    {
        SpirvVertexBytecode = vertexSpirv;
        SpirvFragmentBytecode = fragmentSpirv;
        VertexEntryPoint = vertexEntryPoint;
        FragmentEntryPoint = fragmentEntryPoint;

        VertexModule = CreateModule(vertexSpirv);
        FragmentModule = CreateModule(fragmentSpirv);
    }

    private ShaderModule CreateModule(ReadOnlySpan<byte> spirvCode)
    {
        if (spirvCode.IsEmpty || (spirvCode.Length % 4) != 0)
            throw new ArgumentException("Ungültiger SPIR-V Bytecode (muss durch 4 teilbar sein).");

        fixed (byte* pCode = spirvCode)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirvCode.Length,
                PCode = (uint*)pCode
            };

            if (_context.Api.CreateShaderModule(_context.Device, &createInfo, null, out var module) != Result.Success)
                throw new InvalidOperationException("VkShaderModule konnte nicht erstellt werden.");

            return module;
        }
    }

    /// <summary>Zerstört beide nativen <see cref="ShaderModule"/>-Handles deterministisch.</summary>
    public void Dispose()
    {
        if (_context.Device.Handle == 0)
            return;

        var vk = _context.Api;
        if (VertexModule.Handle != 0)
        {
            vk.DestroyShaderModule(_context.Device, VertexModule, null);
            VertexModule = default;
        }
        if (FragmentModule.Handle != 0)
        {
            vk.DestroyShaderModule(_context.Device, FragmentModule, null);
            FragmentModule = default;
        }
    }
}
