namespace MosaicLiveGenerator;

/// <summary>
/// Selects the H.264 encoder used for the composed output.
/// </summary>
public enum HwAccel
{
    /// <summary>Software encode via libx264. Works everywhere.</summary>
    None,

    /// <summary>NVIDIA NVENC (h264_nvenc). Requires a recent NVIDIA GPU + driver.</summary>
    Nvidia,

    /// <summary>Intel Quick Sync (h264_qsv). Requires an Intel iGPU or Arc dGPU.</summary>
    Intel,
}
