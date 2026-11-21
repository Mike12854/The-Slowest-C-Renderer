using System;
using Silk.NET.Vulkan;

namespace Vulkan.Core;

/// <summary>
/// Hilfsklasse für GPU-Speicherabfragen und Memory-Type-Resolution auf dem ausgewählten <see cref="PhysicalDevice"/>.
/// (Hinweis: Identische Logik existiert auch direkt auf <see cref="VulkanContext.FindMemoryType"/> –
/// falls du irgendwann aufräumst, kannst du eins von beiden einsparen.)
/// </summary>
internal static class VulkanMemoryHelper
{
    /// <summary>
    /// Ermittelt den ersten kompatiblen Vulkan-MemoryType-Index anhand der vom Treiber gelieferten
    /// <paramref name="typeFilter"/>-Bitmaske und den gewünschten <paramref name="properties"/>
    /// (z. B. <c>DeviceLocalBit</c> für schnelles VRAM oder <c>HostVisibleBit | HostCoherentBit</c> für CPU-Uploads).
    /// </summary>
    public static uint FindMemoryType(VulkanContext context, uint typeFilter, MemoryPropertyFlags properties)
    {
        context.Api.GetPhysicalDeviceMemoryProperties(context.PhysicalDevice, out var memProperties);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Kein passender GPU-Speichertyp gefunden.");
    }
}