using System;
using System.Linq;

namespace JSAI.WinApp;

/// <summary>
/// Centralizes the rules that decide which configured model a workflow node actually uses.
/// Keeping this logic in one place avoids UI, runtime, and direct-generation panels drifting apart.
/// </summary>
public static class WorkflowModelResolver
{
    public static ModelInfo? ResolveExecutionModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
    {
        if (category == ModelCategory.Image &&
            string.Equals(node.Type, WorkflowNodeCatalog.StoryboardImage, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveStoryboardTextToImageModel(settings, node);
        }

        return ResolveSelectedModel(settings, node, category);
    }

    public static ModelInfo? ResolveDirectStudioPromptTextModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveExplicitPreferredModel(settings, node, ModelCategory.Text)
               ?? ModelConfig.GetPreferredLocalTextModel(settings)
               ?? ResolveGlobalSelectedModel(settings, node, ModelCategory.Text)
               ?? FirstUsableModel(settings, ModelCategory.Text);
    }

    public static ModelInfo? ResolveDirectStudioImageExecutionModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveExplicitPreferredModel(settings, node, ModelCategory.Image)
               ?? ModelConfig.GetPreferredLocalImageModel(settings)
               ?? ResolveGlobalSelectedModel(settings, node, ModelCategory.Image)
               ?? FirstUsableModel(settings, ModelCategory.Image);
    }

    public static ModelInfo? ResolveDirectStudioVideoExecutionModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveExplicitPreferredModel(settings, node, ModelCategory.Video)
               ?? ModelConfig.GetPreferredLocalVideoModel(settings)
               ?? ResolveGlobalSelectedModel(settings, node, ModelCategory.Video)
               ?? FirstUsableModel(settings, ModelCategory.Video);
    }

    public static ModelInfo? ResolveStoryboardVideoExecutionModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveSelectedModel(settings, node, ModelCategory.Video)
               ?? ModelConfig.GetPreferredLocalVideoModel(settings)
               ?? FirstUsableModel(settings, ModelCategory.Video);
    }

    public static ModelInfo? ResolveSelectedModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
    {
        return ResolvePreferredOrDefaultNodeModel(settings, node, category)
               ?? ResolveGlobalSelectedModel(settings, node, category);
    }

    public static ModelInfo? ResolveCharacterTextToImageModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveCharacterImageSlotModel(settings, node, node.Params?.CharacterTextToImageModelId)
               ?? ResolveSelectedModel(settings, node, ModelCategory.Image);
    }

    public static ModelInfo? ResolveCharacterImageToImageModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveCharacterImageSlotModel(settings, node, node.Params?.CharacterImageToImageModelId)
               ?? ResolveCharacterTextToImageModel(settings, node);
    }

    public static ModelInfo? ResolveStoryboardTextToImageModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveStoryboardImageSlotModel(settings, node, node.Params?.StoryboardTextToImageModelId)
               ?? ResolveSelectedModel(settings, node, ModelCategory.Image)
               ?? ModelConfig.GetStoryboardImageWorkflowModel(settings);
    }

    public static ModelInfo? ResolveStoryboardImageToImageModel(ModelSettings settings, WorkflowNode node)
    {
        return ResolveStoryboardImageSlotModel(settings, node, node.Params?.StoryboardImageToImageModelId)
               ?? ResolveStoryboardTextToImageModel(settings, node);
    }

    public static ModelInfo? ResolvePreferredOrDefaultNodeModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
    {
        var preferredId = node.Params?.GetPreferredModelId(category) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferredId))
        {
            preferredId = node.Params?.PreferredModelId ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            if (category == ModelCategory.Video &&
                string.Equals(preferredId, ModelConfig.RelayVideoModeModelId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var preferred = FindModel(settings, category, preferredId);
            if (preferred != null)
            {
                return ApplyRelayConfiguration(settings, node, category, preferred);
            }
        }

        var nodeDefaultId = ModelConfig.GetDefaultModelForNodeType(settings, node.Type);
        if (!string.IsNullOrWhiteSpace(nodeDefaultId))
        {
            var nodeDefault = FindModel(settings, category, nodeDefaultId);
            if (nodeDefault != null)
            {
                return ApplyRelayConfiguration(settings, node, category, nodeDefault);
            }
        }

        return null;
    }

    public static ModelInfo? ResolveGlobalSelectedModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
    {
        var selectedId = category switch
        {
            ModelCategory.Text => settings.SelectedTextModel,
            ModelCategory.Image => settings.SelectedImageModel,
            ModelCategory.Video => settings.SelectedVideoModel,
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(selectedId))
        {
            return null;
        }

        var selected = FindModel(settings, category, selectedId);
        return ApplyRelayConfiguration(settings, node, category, selected);
    }

    public static ModelInfo? ResolveExplicitPreferredModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
    {
        var preferredId = node.Params?.GetPreferredModelId(category) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferredId))
        {
            preferredId = node.Params?.PreferredModelId ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(preferredId))
        {
            return null;
        }

        if (category == ModelCategory.Video &&
            string.Equals(preferredId, ModelConfig.RelayVideoModeModelId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ApplyRelayConfiguration(settings, node, category, FindModel(settings, category, preferredId));
    }

    public static ModelInfo? BuildRelayVideoExecutionModel(ModelSettings settings, WorkflowNode node)
    {
        if (ModelConfig.LocalOnlyMode)
        {
            return null;
        }

        var providerCode = node.Params?.StoryboardVideoPlatform ?? string.Empty;
        var relayApi = ModelConfig.GetRelayApi(settings, providerCode);
        if (relayApi == null || !relayApi.Enabled || string.IsNullOrWhiteSpace(relayApi.BaseUrl))
        {
            return null;
        }

        var requestedModelName = GetStoryboardVideoRequestedModelName(node, null);
        var providerName = string.IsNullOrWhiteSpace(relayApi.Name)
            ? WorkflowNodeParameters.GetStoryboardVideoPlatformDisplayName(providerCode)
            : relayApi.Name;
        var familyName = WorkflowNodeParameters.GetStoryboardVideoModelFamilyDisplayName(node.Params?.StoryboardVideoModelFamily);
        var subModelName = WorkflowNodeParameters.GetStoryboardVideoSubModelDisplayName(node.Params?.StoryboardVideoSubModel);

        return new ModelInfo
        {
            Id = string.IsNullOrWhiteSpace(requestedModelName) ? providerCode : requestedModelName,
            Name = $"{providerName} / {familyName} / {subModelName}",
            Url = relayApi.BaseUrl,
            Key = relayApi.Key,
            Category = ModelCategory.Video,
            Source = ModelEndpointSource.Cloud,
        };
    }

    public static string GetStoryboardVideoRequestedModelName(WorkflowNode node, ModelInfo? credentialModel)
    {
        var subModel = (node.Params?.StoryboardVideoSubModel ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(subModel))
        {
            return string.Equals(subModel, "grok-video-3-10s", StringComparison.OrdinalIgnoreCase)
                ? "grok-video-3"
                : subModel;
        }

        var family = (node.Params?.StoryboardVideoModelFamily ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(family))
        {
            return family;
        }

        return credentialModel?.Id ?? string.Empty;
    }

    public static string FormatModelDisplayName(ModelInfo model)
    {
        return $"[{ModelConfig.GetModelSourceDisplayName(model)}] {model.Name}";
    }

    private static ModelInfo? FindModel(ModelSettings settings, ModelCategory category, string id)
    {
        return settings.Models.FirstOrDefault(model =>
            model.Category == category &&
            ModelConfig.MatchesModelSelector(model, id));
    }

    private static ModelInfo? ResolveCharacterImageSlotModel(ModelSettings settings, WorkflowNode node, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        return ApplyRelayConfiguration(settings, node, ModelCategory.Image, FindModel(settings, ModelCategory.Image, selector));
    }

    private static ModelInfo? ResolveStoryboardImageSlotModel(ModelSettings settings, WorkflowNode node, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        return ApplyRelayConfiguration(settings, node, ModelCategory.Image, FindModel(settings, ModelCategory.Image, selector));
    }

    private static ModelInfo? FirstUsableModel(ModelSettings settings, ModelCategory category)
    {
        return settings.Models.FirstOrDefault(model =>
            model.Category == category &&
            !string.IsNullOrWhiteSpace(model.Url) &&
            ModelConfig.IsLocalEndpointUrl(model.Url));
    }

    private static ModelInfo? ApplyRelayConfiguration(ModelSettings settings, WorkflowNode node, ModelCategory category, ModelInfo? model)
    {
        if (model == null)
        {
            return null;
        }

        if (ModelConfig.LocalOnlyMode)
        {
            return ModelConfig.IsLocalEndpointUrl(model.Url) ? model : null;
        }

        var providerCode = string.Empty;
        if (category == ModelCategory.Video &&
            string.Equals(node.Type, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase))
        {
            providerCode = node.Params?.StoryboardVideoPlatform ?? string.Empty;
        }

        return ModelConfig.ApplyRelayOverrides(settings, model, providerCode);
    }
}
