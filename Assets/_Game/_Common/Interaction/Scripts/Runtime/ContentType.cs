namespace Interaction
{
    /// <summary>
    /// 内容类型枚举，用于内容适配层与效果系统区分处理。
    /// </summary>
    public enum ContentType
    {
        /// <summary> 3D 模型内容（包括用模型壳显示的图片等）。</summary>
        Model,
        /// <summary> 视频内容（VideoPlayer 等）。</summary>
        Video,
        /// <summary> 粒子系统内容（ParticleSystem 为主）。</summary>
        Particle,
        /// <summary> VFX Graph 等特效内容。</summary>
        VFX
    }
}
