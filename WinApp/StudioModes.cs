namespace JSAI.WinApp
{
    public enum ProjectWorkspaceMode
    {
        AiAnimeProject,
        DirectStudio,
    }

    public enum ProjectLaunchMode
    {
        None,
        AiAnimeProject,
        TextToImage,
        TextToVideo,
        TextImageToVideo,
        LoadProject,
    }

    public enum QuickStudioMode
    {
        TextToImage,
        TextToVideo,
        TextImageToVideo,
    }

    public sealed class DirectGenerationResult
    {
        public string ArtifactPath { get; set; } = string.Empty;

        public string PositivePrompt { get; set; } = string.Empty;

        public string NegativePrompt { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string ExecutionModelName { get; set; } = string.Empty;

        public string PromptModelName { get; set; } = string.Empty;
    }
}
