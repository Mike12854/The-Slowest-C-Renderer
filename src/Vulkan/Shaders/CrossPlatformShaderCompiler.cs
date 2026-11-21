using System;
using System.Runtime.InteropServices;
using API.Enums;
using Silk.NET.Shaderc;

namespace Vulkan.Shaders;

/// <summary>
/// Plattformspezifischer (Windows &amp; Linux) Runtime-Shader-Compiler auf Basis von <see cref="Shaderc"/> (<c>libshaderc</c>).
/// Kompiliert HLSL- oder GLSL-Quellcode direkt im Speicher in hochoptimierten Vulkan-1.3-SPIR-V-Bytecode
/// (<see cref="OptimizationLevel.Performance"/>) und gibt alle nativen Compiler-Handles deterministisch per <c>finally</c> frei.
/// </summary>
internal sealed unsafe class CrossPlatformShaderCompiler : IDisposable
{
    private readonly Shaderc _shaderc;
    private readonly Compiler* _compiler;

    /// <summary>Initialisiert die native <c>libshaderc</c>-Compiler-Instanz.</summary>
    public CrossPlatformShaderCompiler()
    {
        _shaderc = Shaderc.GetApi();
        _compiler = _shaderc.CompilerInitialize();
        if (_compiler == null)
            throw new InvalidOperationException("Shaderc Compiler konnte nicht initialisiert werden.");
    }

    /// <summary>
    /// Kompiliert eine einzelne Shader-Stage (<paramref name="stage"/>) aus Quellcode in einen Vulkan-1.3-SPIR-V-Bytecode-Buffer.
    /// </summary>
    /// <param name="sourceCode">Der HLSL- oder GLSL-Quellcode.</param>
    /// <param name="stage">Ziel-Shader-Stage (z. B. <see cref="ShaderKind.VertexShader"/> oder <see cref="ShaderKind.FragmentShader"/>).</param>
    /// <param name="entryPoint">Name der Einstiegsfunktion im Shader (z. B. <c>"VSMain"</c>, <c>"PSMain"</c> oder <c>"main"</c>).</param>
    /// <param name="language">Quellsprache (<see cref="EShaderLanguage.HLSL"/> oder <see cref="EShaderLanguage.GLSL"/>).</param>
    /// <param name="debugName">Bezeichner für präzise Fehlermeldungen bei Syntax-/Kompilierungsfehlern.</param>
    /// <returns>Der fertige SPIR-V-Bytecode als <see cref="byte"/>-Array.</returns>
    /// <exception cref="InvalidOperationException">Wird geworfen, wenn <c>libshaderc</c> einen Kompilierungsfehler meldet.</exception>
    public byte[] CompileStage(
        string sourceCode,
        ShaderKind stage,
        string entryPoint,
        EShaderLanguage language,
        string debugName = "shader")
    {
        var options = _shaderc.CompileOptionsInitialize();
        try
        {
            var srcLang = language == EShaderLanguage.HLSL
                ? SourceLanguage.Hlsl
                : SourceLanguage.Glsl;

            _shaderc.CompileOptionsSetSourceLanguage(options, srcLang);
            _shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
            _shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan13);

            var result = _shaderc.CompileIntoSpv(
                _compiler,
                sourceCode,
                (nuint)System.Text.Encoding.UTF8.GetByteCount(sourceCode),
                stage,
                debugName,
                entryPoint,
                options);

            try
            {
                var status = _shaderc.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    var errMsg = Marshal.PtrToStringAnsi((nint)_shaderc.ResultGetErrorMessage(result));
                    throw new InvalidOperationException($"Shader-Kompilierung fehlgeschlagen ({stage}):\n{errMsg}");
                }

                nuint length = _shaderc.ResultGetLength(result);
                byte* bytesPtr = _shaderc.ResultGetBytes(result);

                var spirv = new byte[length];
                Marshal.Copy((nint)bytesPtr, spirv, 0, (int)length);
                return spirv;
            }
            finally
            {
                _shaderc.ResultRelease(result);
            }
        }
        finally
        {
            _shaderc.CompileOptionsRelease(options);
        }
    }

    /// <summary>Gibt die native <c>libshaderc</c>-Instanz und die Silk.NET-API-Bindings frei.</summary>
    public void Dispose()
    {
        if (_compiler != null)
        {
            _shaderc.CompilerRelease(_compiler);
        }
        _shaderc.Dispose();
    }
}
