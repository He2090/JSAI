using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JSAI.WinApp;

public sealed class WorkflowPromptDispatchedEventArgs : EventArgs
{
	public WorkflowPromptDispatchedEventArgs(string moduleName, string modelName, string prompt)
	{
		ModuleName = moduleName ?? string.Empty;
		ModelName = modelName ?? string.Empty;
		Prompt = prompt ?? string.Empty;
		DispatchedAt = DateTime.Now;
	}

	public string ModuleName { get; }

	public string ModelName { get; }

	public string Prompt { get; }

	public DateTime DispatchedAt { get; }
}

public sealed class StoryboardVideoClipGeneratedEventArgs : EventArgs
{
	public StoryboardVideoClipGeneratedEventArgs(int index, int total, StoryboardVideoGeneratedClip clip)
	{
		Index = Math.Max(0, index);
		Total = Math.Max(1, total);
		Clip = clip;
	}

	public int Index { get; }

	public int Total { get; }

	public StoryboardVideoGeneratedClip Clip { get; }
}

public sealed class WorkflowRuntimeService
{
	private const string VideoNoVisibleTextPromptGuard = "No visible text, subtitles, captions, logos, watermark, UI, signs, posters, labels, or screen text. Dialogue and subtitles are post-production reference only. Keep all surfaces clean and natural.";
	private const string VideoNoVisibleTextNegativePrompt = "subtitles, captions, visible text, watermark";

	private sealed class DisposableBitmapCollection : IDisposable
	{
		public List<Bitmap> Items { get; }

		public DisposableBitmapCollection(IEnumerable<string> imagePaths)
		{
			Items = (from path in imagePaths
				where !string.IsNullOrWhiteSpace(path) && File.Exists(path)
				select new Bitmap(path)).ToList();
		}

		public void Dispose()
		{
			foreach (Bitmap item in Items)
			{
				item.Dispose();
			}
		}
	}

	private readonly record struct ComfyUiOutputImage(string Filename, string Subfolder, string Type);

	private readonly record struct ComfyUiOutputVideo(string Filename, string Subfolder, string Type);

	private readonly record struct GeneratedArtifact(string Path, string Description);

	public Func<StoryboardVideoClipGeneratedEventArgs, CancellationToken, Task<bool>>? ConfirmStoryboardVideoContinueAsync { get; set; }

	private const string ComfyUiTemplateFileName = "Get_imageAI.json";

	private const string ComfyUiReferenceTemplateFileName = "FaceimgtoimgWorkflow.json";

	private const string ComfyUiImageTextToVideoTemplateFileName = "ImgandTextTovideo1.0.json";

	private static readonly Regex ComfyUiWorkflowPlaceholderRegex = new("\\{\\{\\s*([A-Za-z0-9_.-]+)\\s*\\}\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private const string CharacterFaceHairConsistencyPrompt = "strict same-person identity lock, use the reference portrait as the only identity source, preserve identical gender, age, face shape, jawline, cheekbone structure, nose bridge, eye spacing, eye shape, mouth width, hairline, bangs, hair length, hair volume, hair parting, hairstyle silhouette, hair color and visible clothing collar from the reference portrait, only eyebrows, eyelids and mouth expression may change, do not redesign the face, do not change hairstyle, do not change gender, do not create a different person";

	private const string CharacterSingleExpressionPortraitPrompt = "this image is one isolated expression-cell render only, exactly one centered head, exactly one face, exactly one person, one portrait only, no second head, no third head, no duplicate face, no side-by-side faces, no row of faces, no face collage, no mini portraits, no nested grid, no sub-panels inside the image, no expression sheet inside this image";

	private const string CharacterExpressionBustFramingPrompt = "strict head-and-shoulders portrait framing only, show only head, neck, collar neckline and the top of the shoulders, crop at the collarbone, shoulders-above framing, never show chest, bust, torso, upper chest, clothing body, jacket buttons below collarbone, arms, hands, waist, belt, pants, hips, full torso, bag straps, props, or anything below the shoulders";

	private const string CharacterExpressionCleanBackgroundPrompt = "strict clean portrait background lock, seamless plain light gray or white studio background only, empty background with no objects, no real environment, no scenery, no workplace, no vehicle, no food truck, no street, no room, no wall details, no door, no window, no furniture, no signs, no posters, no shelves, no props, no background objects";

	private const string CharacterExpressionOnlyChangePrompt = "only change the facial expression, keep the exact same person from the reference portrait, same sex and gender presentation, same face identity, same facial proportions, same haircut, same hair length, same hairline, same age, same clothing collar, same camera angle, same exact straight-on front view, same crop, same lighting, same plain background, same full-color finished rendering style, same skin texture and color saturation, face parallel to camera, head level, no head turn, no head tilt, do not turn into sketch, line art, pencil drawing, grayscale, washed-out draft, or a different art style";

	private const string CharacterExpressionVariationPrompt = "the requested emotion must be strong, obvious, and readable at first glance, do not repeat the neutral reference expression, clearly change eyebrow shape, eyelid tension, eye openness, gaze focus, cheek tension, mouth shape, lip tension, and jaw tension to match the requested emotion, do not keep the same face mood in every cell";

	private const string CharacterThreeViewFullBodyPrompt = "mandatory complete full-body framing, strict orthographic turnaround reference, exact requested camera angle only, show the entire character from the top of the head to the soles of the shoes in every view, include complete head, torso, both legs, both feet and shoes, leave small margin around head and feet, body upright and centered, shoulders level, hips level, feet flat on the ground, no portrait, no bust, no half-body, no waist-up, no knee-up, no cropped head, no cropped feet, no partial body, no walking pose, no stepping pose, no torso twist, no contrapposto, no dynamic pose, no fashion pose";

	private const string CharacterThreeViewSingleViewPrompt = "this image must contain exactly one single full-body standing character and exactly one orthographic view only, never two views in one frame, never multiple figures, never a portrait insert, never a close-up face crop, never a split composition inside the panel, the character must remain centered and complete from head to toe";

	private const string CharacterThreeViewStrictSidePrompt = "true 90-degree left side standing silhouette, shoulders stacked in profile, hips stacked in profile, only one eye visible, only one ear visible, only one side of the nose visible, nose points left, toes point left, chest thickness visible as a narrow side edge, no front torso visible, no symmetrical jacket lapels, no looking at camera";

	private const string CharacterThreeViewCleanBackgroundPrompt = "strict clean background lock, seamless plain light gray or white studio background only, empty background with no objects, no real environment, no scenery, no workplace, no vehicle, no food truck, no street, no room, no wall details, no door, no window, no furniture, no signs, no posters, no shelves, no props, no background objects";

	private const string CharacterThreeViewNoBagPrompt = "strict no-bag lock, the character must not wear, carry, hold, or have any bag of any kind, no backpack, shoulder bag, tote bag, handbag, purse, satchel, messenger bag, crossbody bag, sling bag, waist bag, pouch, luggage, bag strap, shoulder strap, crossbody strap, backpack straps, bag handles, or bag hardware; ignore and remove all bags and bag-like accessories from reference images, costume notes, profession context, and upstream text";

	private const string CharacterThreeViewNoExternalObjectPrompt = "strict only-person lock, the image may contain only the character body, hair, wearable clothing and shoes; no external objects of any kind, no microphone, mic, mic stand, camera, tripod, light stand, boom pole, phone, tablet, weapon, tool, umbrella, staff, cane, suitcase, box, paper, folder, clipboard, desk, chair, stool, podium, cable, badge prop, handheld prop, floating prop, foreground prop, background prop, object beside the character, object near the character, or profession equipment; empty hands only";

	private static readonly HttpClient HttpClient = new HttpClient
	{
		Timeout = TimeSpan.FromMinutes(3.0)
	};

	public static event EventHandler<WorkflowPromptDispatchedEventArgs>? PromptDispatched;

	private static void PublishPrompt(string? moduleName, ModelInfo? model, string? prompt, string? negativePrompt = null)
	{
		string normalizedPrompt = (prompt ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalizedPrompt) && string.IsNullOrWhiteSpace(negativePrompt))
		{
			return;
		}

		var builder = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(normalizedPrompt))
		{
			builder.AppendLine(normalizedPrompt);
		}

		if (!string.IsNullOrWhiteSpace(negativePrompt))
		{
			if (builder.Length > 0)
			{
				builder.AppendLine();
			}

			builder.AppendLine("Negative Prompt:");
			builder.AppendLine(negativePrompt.Trim());
		}

		PromptDispatched?.Invoke(
			null,
			new WorkflowPromptDispatchedEventArgs(
				string.IsNullOrWhiteSpace(moduleName) ? "模型调用提示词" : moduleName.Trim(),
				FormatPromptModelName(model),
				builder.ToString().Trim()));
	}

	private static string FormatPromptModelName(ModelInfo? model)
	{
		if (model == null)
		{
			return string.Empty;
		}

		if (!string.IsNullOrWhiteSpace(model.Name) && !string.IsNullOrWhiteSpace(model.Id))
		{
			return $"{model.Name} ({model.Id})";
		}

		return model.Name.OrDefault(model.Id);
	}

	private static IReadOnlyList<string>? BuildReferenceImageList(string? referenceImagePath)
	{
		if (string.IsNullOrWhiteSpace(referenceImagePath))
		{
			return null;
		}

		string text = referenceImagePath.Trim();
		if (Uri.TryCreate(text, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
		{
			return new string[1] { text };
		}

		if (!File.Exists(text))
		{
			return null;
		}

		return new string[1] { text };
	}

	private static IReadOnlyList<string>? BuildReferenceImageList(IEnumerable<string>? referenceImagePaths)
	{
		if (referenceImagePaths == null)
		{
			return null;
		}

		List<string> list = new List<string>();
		foreach (string referenceImagePath in referenceImagePaths)
		{
			IReadOnlyList<string> item = BuildReferenceImageList(referenceImagePath);
			if (item == null)
			{
				continue;
			}

			foreach (string path in item)
			{
				if (!list.Contains(path, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(path);
				}
			}
		}

		return list.Count == 0 ? null : list;
	}

	private static IReadOnlyList<string>? CollectStoryboardReferenceImages(IReadOnlyList<CharacterDesignEntry> entries)
	{
		if (entries == null || entries.Count == 0)
		{
			return null;
		}

		List<string> list = new List<string>();
		foreach (CharacterDesignEntry entry in entries)
		{
			string path = entry.HasThreeViewSheet ? entry.ThreeViewSheetPath : (entry.HasExpressionSheet ? entry.ExpressionSheetPath : (entry.HasReferencePortrait ? entry.ReferencePortraitPath : string.Empty));
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(path);
			}
		}

		return list.Count == 0 ? null : list;
	}

	private static string BuildStoryboardCharacterReferenceBasePrompt(CharacterDesignEntry entry)
	{
		List<string> parts = new List<string>();
		var appearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
		var costumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);
		if (!string.IsNullOrWhiteSpace(appearancePrompt))
		{
			parts.Add(appearancePrompt);
		}
		else if (!string.IsNullOrWhiteSpace(entry.CompactSummary))
		{
			parts.Add(entry.CompactSummary.Trim());
		}

		if (!string.IsNullOrWhiteSpace(costumeNotes))
		{
			parts.Add(costumeNotes);
		}

		if (!string.IsNullOrWhiteSpace(entry.VisualTags))
		{
			parts.Add(entry.VisualTags.Trim());
		}

		if (parts.Count == 0)
		{
			parts.Add("anime character portrait");
		}

		return string.Join(", ", parts.Where((string value) => !string.IsNullOrWhiteSpace(value)));
	}

	private static async Task<IReadOnlyList<string>?> CollectStoryboardComfyUiReferenceImagesAsync(ModelInfo model, WorkflowNode node, IReadOnlyList<CharacterDesignEntry> entries, CancellationToken cancellationToken)
	{
		if (entries == null || entries.Count == 0)
		{
			return null;
		}

		List<string> list = new List<string>();
		foreach (CharacterDesignEntry entry in entries)
		{
			string referencePortraitPath = await EnsureComfyUiCharacterReferencePortraitAsync(model, node, entry, BuildStoryboardCharacterReferenceBasePrompt(entry), WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry), cancellationToken, "Storyboard/Character Reference");
			if (!string.IsNullOrWhiteSpace(referencePortraitPath) && File.Exists(referencePortraitPath) && !list.Contains(referencePortraitPath, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(referencePortraitPath);
			}
		}

		return list.Count == 0 ? null : list;
	}

	public async Task<DirectGenerationResult> GenerateDirectImageAsync(string userPrompt, string imageMode, ModelInfo promptModel, ModelInfo imageModel, bool optimizeForCloud, int width, int height, CancellationToken cancellationToken, string? referenceImagePath = null, string? positivePromptOverride = null, string? negativePromptOverride = null)
	{
		if (string.IsNullOrWhiteSpace(userPrompt))
		{
			throw new InvalidOperationException("Please enter an image description.");
		}
		if (promptModel == null || string.IsNullOrWhiteSpace(promptModel.Url))
		{
			throw new InvalidOperationException("No text model configured.");
		}
		if (imageModel == null || string.IsNullOrWhiteSpace(imageModel.Url))
		{
			throw new InvalidOperationException("No image model configured.");
		}

		WorkflowNode node = new WorkflowNode
		{
			Id = "quick_image_" + Guid.NewGuid().ToString("N"),
			Type = WorkflowNodeCatalog.CharacterView,
			Params = new WorkflowNodeParameters
			{
				Input = userPrompt.Trim(),
				DirectImageMode = imageMode
			}
		};
		node.Params.EnsureDefaults(node.Type);

		string positivePrompt;
		string negativePrompt;
		if (!string.IsNullOrWhiteSpace(positivePromptOverride))
		{
			positivePrompt = positivePromptOverride.Trim();
			negativePrompt = negativePromptOverride.OrDefault(string.Empty).Trim();
		}
		else
		{
			positivePrompt = WorkflowExecutor.NormalizeTextResult(node.Type, await ExecuteTextCompletionAsync(promptModel, BuildDirectImagePromptRequest(userPrompt, imageMode, imageModel, optimizeForCloud), cancellationToken, 0.4, null, "Text-to-Image/Positive Prompt"));
			negativePrompt = WorkflowExecutor.NormalizeTextResult(node.Type, await ExecuteTextCompletionAsync(promptModel, BuildDirectImageNegativePromptRequest(userPrompt, imageMode, imageModel, optimizeForCloud), cancellationToken, 0.25, null, "Text-to-Image/Negative Prompt"));
			(positivePrompt, negativePrompt) = ApplyDirectImagePromptGuards(userPrompt, imageMode, positivePrompt, negativePrompt);
		}
		width = Math.Max(256, width);
		height = Math.Max(256, height);
		bool useLocalEndpoint = ModelConfig.IsLocalEndpointUrl(imageModel.Url);
		string submittedNegativePrompt = useLocalEndpoint ? negativePrompt : string.Empty;
		GeneratedArtifact artifact;
		IReadOnlyList<string>? referenceImages = BuildReferenceImageList(referenceImagePath);
		string moduleName = !string.IsNullOrWhiteSpace(referenceImagePath) ? "Image-to-Image" : "Text-to-Image";
		if (IsComfyUiLike(imageModel.Url))
		{
			artifact = await ExecuteComfyUiImageAsync(imageModel, node, positivePrompt, submittedNegativePrompt, cancellationToken, width, height, "quick_image", BuildStableSeed(node.Id, "1", "1"), null, referenceImages);
		}
		else if (IsYunWuLike(imageModel.Url))
		{
			artifact = await ExecuteYunWuImageAsync(imageModel, node, positivePrompt, submittedNegativePrompt, cancellationToken, $"{width}x{height}", moduleName, referenceImages);
		}
		else if (IsStableDiffusionLike(imageModel.Url))
		{
			artifact = await ExecuteStableDiffusionImageAsync(imageModel, node, positivePrompt, cancellationToken, width, height, submittedNegativePrompt, moduleName);
		}
		else
		{
			artifact = await ExecuteOpenAiCompatibleImageAsync(imageModel, node, positivePrompt, cancellationToken, $"{width}x{height}", moduleName, submittedNegativePrompt);
		}
		return new DirectGenerationResult
		{
			ArtifactPath = artifact.Path,
			PositivePrompt = positivePrompt,
			NegativePrompt = submittedNegativePrompt,
			Description = artifact.Description,
			ExecutionModelName = imageModel.Name,
			PromptModelName = promptModel.Name
		};
	}

	public async Task<DirectGenerationResult> GenerateDirectVideoAsync(string userPrompt, string? referenceImagePath, ModelInfo promptModel, ModelInfo videoModel, bool optimizeForCloud, string aspectRatio, int durationSeconds, string quality, CancellationToken cancellationToken, string? videoPlatform = null, string? videoModelFamily = null, string? videoSubModel = null, bool needSound = false, string? positivePromptOverride = null, string? negativePromptOverride = null)
	{
		if (string.IsNullOrWhiteSpace(userPrompt))
		{
			throw new InvalidOperationException("Please enter a video description.");
		}
		if (promptModel == null || string.IsNullOrWhiteSpace(promptModel.Url))
		{
			throw new InvalidOperationException("No text model configured.");
		}
		if (videoModel == null || string.IsNullOrWhiteSpace(videoModel.Url))
		{
			throw new InvalidOperationException("No video model configured.");
		}

		WorkflowNode node = new WorkflowNode
		{
			Id = "quick_video_" + Guid.NewGuid().ToString("N"),
			Type = WorkflowNodeCatalog.StoryboardVideo,
			Params = new WorkflowNodeParameters
			{
				Input = userPrompt.Trim(),
				StoryboardVideoAspectRatio = (string.Equals(aspectRatio, "9:16", StringComparison.OrdinalIgnoreCase) ? "9:16" : "16:9"),
				StoryboardVideoDurationSeconds = Math.Max(5, durationSeconds),
				StoryboardVideoQuality = string.IsNullOrWhiteSpace(quality) ? "HD" : quality.Trim(),
				StoryboardVideoPromptLanguage = "zh"
			}
		};
		node.Params.EnsureDefaults(node.Type);
		node.Params.StoryboardVideoPlatform = string.IsNullOrWhiteSpace(videoPlatform) ? InferDirectVideoPlatform(videoModel) : videoPlatform.Trim();
		node.Params.StoryboardVideoModelFamily = string.IsNullOrWhiteSpace(videoModelFamily) ? InferDirectVideoModelFamily(videoModel) : videoModelFamily.Trim();
		node.Params.StoryboardVideoSubModel = string.IsNullOrWhiteSpace(videoSubModel)
			? (string.IsNullOrWhiteSpace(videoModel.Id) ? WorkflowNodeParameters.GetDefaultStoryboardVideoSubModel(node.Params.StoryboardVideoModelFamily) : videoModel.Id.Trim())
			: videoSubModel.Trim();
		node.Params.StoryboardVideoNeedSound = needSound;

		string positivePrompt;
		string negativePrompt;
		if (!string.IsNullOrWhiteSpace(positivePromptOverride))
		{
			positivePrompt = positivePromptOverride.Trim();
			negativePrompt = negativePromptOverride.OrDefault(string.Empty).Trim();
		}
		else
		{
			positivePrompt = WorkflowExecutor.NormalizeTextResult(node.Type, await ExecuteTextCompletionAsync(promptModel, BuildDirectVideoPromptRequest(userPrompt, referenceImagePath, videoModel, optimizeForCloud), cancellationToken, 0.4, null, "Text-to-Video/Positive Prompt"));
			negativePrompt = WorkflowExecutor.NormalizeTextResult(node.Type, await ExecuteTextCompletionAsync(promptModel, BuildDirectVideoNegativePromptRequest(userPrompt, videoModel, optimizeForCloud), cancellationToken, 0.25, null, "Text-to-Video/Negative Prompt"));
		}
		(positivePrompt, negativePrompt) = ApplyDirectVideoPromptGuards(positivePrompt, negativePrompt);
		if (!ModelConfig.IsLocalEndpointUrl(videoModel.Url))
		{
			negativePrompt = string.Empty;
		}
		node.Params.StoryboardVideoPrompt = positivePrompt;
		node.Params.StoryboardVideoNegativePrompt = negativePrompt;
		GeneratedArtifact artifact = await ExecuteVideoTaskAsync(videoModel, node, positivePrompt, referenceImagePath, cancellationToken);
		return new DirectGenerationResult
		{
			ArtifactPath = artifact.Path,
			PositivePrompt = positivePrompt,
			NegativePrompt = negativePrompt,
			Description = artifact.Description,
			ExecutionModelName = videoModel.Name,
			PromptModelName = promptModel.Name
		};
	}

	private async Task<bool> ExecuteDirectStudioNodeAsync(WorkflowNode node, CancellationToken cancellationToken)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		string userPrompt = (node.Params.Input ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(userPrompt))
		{
			throw new InvalidOperationException("Please enter a prompt.");
		}

		ModelSettings settings = ModelConfig.Load();
		ModelInfo promptModel = ResolveDirectStudioPromptTextModel(settings, node) ?? throw new InvalidOperationException("No text model configured.");
		DirectGenerationResult result;
		if (string.Equals(node.Type, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal) || string.Equals(node.Type, WorkflowNodeCatalog.ImageToImage, StringComparison.Ordinal))
		{
			ModelInfo imageModel = ResolveDirectStudioImageExecutionModel(settings, node)
				?? throw new InvalidOperationException("No image model configured.");

			result = await GenerateDirectImageAsync(
				userPrompt,
				node.Params.DirectImageMode,
				promptModel,
				imageModel,
				optimizeForCloud: !ModelConfig.IsLocalEndpointUrl(imageModel.Url),
				width: Math.Max(256, node.Params.DirectWidth),
				height: Math.Max(256, node.Params.DirectHeight),
				cancellationToken: cancellationToken,
				referenceImagePath: string.Equals(node.Type, WorkflowNodeCatalog.ImageToImage, StringComparison.Ordinal)
					? node.Params.DirectReferenceImagePath
					: null,
				positivePromptOverride: node.Params.DirectPositivePrompt,
				negativePromptOverride: node.Params.DirectNegativePrompt);

			node.Params.DirectExecutionModelName = result.ExecutionModelName;
			node.Params.DirectPromptModelName = result.PromptModelName;
			node.Params.DirectPositivePrompt = result.PositivePrompt;
			node.Params.DirectNegativePrompt = result.NegativePrompt;
			WorkflowExecutor.ApplyArtifactResult(node, userPrompt, result.ArtifactPath, "image", result.Description);
			return true;
		}

		ModelInfo videoModel = ResolveDirectStudioVideoExecutionModel(settings, node)
			?? throw new InvalidOperationException("No video model configured.");

		result = await GenerateDirectVideoAsync(
			userPrompt,
			string.Equals(node.Type, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal) ? node.Params.DirectReferenceImagePath : null,
			promptModel,
			videoModel,
			optimizeForCloud: !ModelConfig.IsLocalEndpointUrl(videoModel.Url),
			aspectRatio: node.Params.DirectAspectRatio,
			durationSeconds: Math.Max(5, node.Params.DirectDurationSeconds),
			quality: node.Params.DirectQuality.OrDefault("HD"),
			cancellationToken: cancellationToken,
			videoPlatform: node.Params.StoryboardVideoPlatform,
			videoModelFamily: node.Params.StoryboardVideoModelFamily,
			videoSubModel: node.Params.StoryboardVideoSubModel,
			needSound: node.Params.StoryboardVideoNeedSound,
			positivePromptOverride: node.Params.DirectPositivePrompt,
			negativePromptOverride: node.Params.DirectNegativePrompt);

		node.Params.DirectExecutionModelName = result.ExecutionModelName;
		node.Params.DirectPromptModelName = result.PromptModelName;
		node.Params.DirectPositivePrompt = result.PositivePrompt;
		node.Params.DirectNegativePrompt = result.NegativePrompt;
		WorkflowExecutor.ApplyArtifactResult(node, userPrompt, result.ArtifactPath, "video", result.Description);
		return true;
	}

	private static string BuildDirectImagePromptRequest(string userPrompt, string imageMode, ModelInfo imageModel, bool optimizeForCloud)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("You are a professional image prompt engineer.");
		stringBuilder.AppendLine("Based on the user's original description, output a final positive prompt ready for image generation.");
		stringBuilder.AppendLine("Output must be a single paragraph of prompt text. No JSON, no lists, no explanations.");
		stringBuilder.AppendLine($"Target image model: {imageModel.Name} ({imageModel.Id})");
		stringBuilder.AppendLine(optimizeForCloud ? "Requirements: Optimize for cloud image models, emphasizing composition control, subject integrity, detail, style, and cinematography." : "Requirements: Optimize for local image models, emphasizing subject, composition, style, materials, lighting, camera work, and executability. Prioritize English tag-style prompts suitable for Stable Diffusion / ComfyUI.");
		stringBuilder.AppendLine("Must preserve the subject category requested by the user. Never change animals into humans, objects into characters, or landscapes into figures.");
		stringBuilder.AppendLine("Must preserve the gender requested by the user. If the user specifies male, do not depict female; if female, do not depict male.");
		stringBuilder.AppendLine("If the user specifies a canine subject (dog, \u72ac, canine, \u9ed1\u80cc, \u9ed1\u8d1d, etc.), the output must explicitly include: dog, canine, quadruped animal, one dog only, no human.");
		if (string.Equals(imageMode, "expression", StringComparison.OrdinalIgnoreCase))
		{
			stringBuilder.AppendLine("This task is a nine-panel expression sheet. Output must explicitly include: 3x3 grid, nine-panel expression sheet, single composite image, all 9 panels visible, consistent subject across all panels. Must not degrade into a single portrait.");
		}
		else if (string.Equals(imageMode, "threeview", StringComparison.OrdinalIgnoreCase))
		{
			stringBuilder.AppendLine("This task is a three-view character sheet. Output must explicitly include: character turnaround sheet, front view, side view, back view, three-panel layout, complete full body from head to toe in every view, feet and shoes fully visible, same subject, single composite image.");
			stringBuilder.AppendLine("Mandatory full-body framing: never output portrait, bust, half-body, waist-up, knee-up, cropped head, cropped feet, or any partial-body view.");
			stringBuilder.AppendLine("Mandatory side view: the second panel must be a true 90-degree standing side profile, with shoulders and hips stacked in profile, only one eye and one ear visible, toes pointing sideways, no front torso, and no face looking at the camera.");
		}
		else
		{
			stringBuilder.AppendLine("If the user requested a multi-panel layout (grid, 3x3, expression sheet, nine-panel, contact sheet, etc.), output must explicitly include: 3x3 grid, nine-panel expression sheet, single composite image, all 9 panels visible. Must not degrade into a single portrait.");
		}
		stringBuilder.AppendLine("If the original description lacks detail, only fill in scene, action, camera, lighting, color, and style consistent with the original subject. Do not replace the subject species or identity.");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("User original description:");
		stringBuilder.AppendLine(userPrompt.Trim());
		return stringBuilder.ToString().Trim();
	}

	private static string BuildDirectImageNegativePromptRequest(string userPrompt, string imageMode, ModelInfo imageModel, bool optimizeForCloud)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("You are an image negative prompt engineer. Output a single comma-separated negative prompt. No explanation.");
		sb.AppendLine($"Model: {imageModel.Name}");
		sb.AppendLine("Exclude: low quality, bad anatomy, extra limbs, watermark, text. Use tag-style negatives.");
		if (string.Equals(imageMode, "expression", StringComparison.OrdinalIgnoreCase))
			sb.AppendLine("Mode: 3x3 expression sheet. Exclude single portrait, cropped grid, missing panels, collage.");
		else if (string.Equals(imageMode, "threeview", StringComparison.OrdinalIgnoreCase))
			sb.AppendLine("Mode: three-view sheet. Exclude single portrait, bust, half-body, waist-up, cropped body, missing views, side-view drift, collage.");
		else
			sb.AppendLine("If multi-panel/grid: exclude single portrait, cropped grid, missing panels, collage.");
		sb.AppendLine();
		sb.AppendLine(userPrompt.Trim());
		return sb.ToString().Trim();
	}

	private static string BuildDirectVideoPromptRequest(string userPrompt, string? referenceImagePath, ModelInfo videoModel, bool optimizeForCloud)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("You are a professional video prompt engineer.");
		stringBuilder.AppendLine("Based on the user's original description, output a final positive prompt ready for video generation.");
		stringBuilder.AppendLine("Output must be a single paragraph of prompt text. No JSON, no lists, no explanations.");
		stringBuilder.AppendLine($"Target video model: {videoModel.Name} ({videoModel.Id})");
		stringBuilder.AppendLine(optimizeForCloud ? "Requirements: Optimize for cloud video models, emphasizing subject motion, camera movement, pacing, frame stability, and style consistency." : "Requirements: Optimize for local video models, emphasizing subject motion, camera work, pacing, lighting, effects, and generation stability.");
		stringBuilder.AppendLine("Use one compact text rule only: no visible text in video pixels; dialogue/subtitles are post-production reference only.");
		if (!string.IsNullOrWhiteSpace(referenceImagePath))
		{
			stringBuilder.AppendLine("This task includes a reference image. Require consistency with the subject appearance, clothing, and color scheme. If the reference image contains text or signage, say only: keep all surfaces clean and natural.");
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("User original description:");
		stringBuilder.AppendLine(userPrompt.Trim());
		return stringBuilder.ToString().Trim();
	}

	private static string BuildDirectVideoNegativePromptRequest(string userPrompt, ModelInfo videoModel, bool optimizeForCloud)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("You are a video negative prompt engineer. Output a single comma-separated negative prompt. No explanation.");
		sb.AppendLine($"Model: {videoModel.Name}");
		sb.AppendLine("Exclude: flickering, blurry, shaking, face drift, visible text, watermark. Keep compact.");
		sb.AppendLine();
		sb.AppendLine(userPrompt.Trim());
		return sb.ToString().Trim();
	}

	private static (string PositivePrompt, string NegativePrompt) ApplyDirectVideoPromptGuards(string positivePrompt, string negativePrompt)
	{
		var adjustedPositive = (positivePrompt ?? string.Empty).Trim();
		var hasTextGuard =
			adjustedPositive.Contains("no visible text", StringComparison.OrdinalIgnoreCase) ||
			adjustedPositive.Contains("no visible written text", StringComparison.OrdinalIgnoreCase) ||
			adjustedPositive.Contains("no subtitles", StringComparison.OrdinalIgnoreCase);
		if (!hasTextGuard)
		{
			adjustedPositive = string.IsNullOrWhiteSpace(adjustedPositive)
				? VideoNoVisibleTextPromptGuard
				: $"{adjustedPositive}. {VideoNoVisibleTextPromptGuard}";
		}

		var adjustedNegative = MergeCommaPromptFragments(negativePrompt, VideoNoVisibleTextNegativePrompt);
		return (adjustedPositive, adjustedNegative);
	}

	private static (string PositivePrompt, string NegativePrompt) ApplyDirectImagePromptGuards(string userPrompt, string imageMode, string positivePrompt, string negativePrompt)
	{
		string normalizedUserPrompt = (userPrompt ?? string.Empty).Trim();
		string normalizedImageMode = NormalizeDirectImageMode(imageMode);
		string adjustedPositive = (positivePrompt ?? string.Empty).Trim();
		string adjustedNegative = (negativePrompt ?? string.Empty).Trim();

		if (!string.IsNullOrWhiteSpace(normalizedUserPrompt))
		{
			adjustedPositive = $"{normalizedUserPrompt}, {adjustedPositive}".Trim().Trim(',');
		}

		if (ContainsDogSubject(normalizedUserPrompt))
		{
			adjustedPositive = MergePromptParts(
				"dog",
				"canine",
				"quadruped animal",
				"one dog only",
				"black shepherd dog",
				"no human",
				adjustedPositive);

			adjustedNegative = MergePromptParts(
				adjustedNegative,
				"human",
				"person",
				"man",
				"woman",
				"people",
				"humanoid",
				"human face",
				"human body",
				"human hands",
				"two-legged human",
				"human crowd");
		}

		bool containsMaleSubject = ContainsMaleSubject(normalizedUserPrompt);
		bool containsFemaleSubject = ContainsFemaleSubject(normalizedUserPrompt);
		if (containsMaleSubject && !containsFemaleSubject)
		{
			adjustedPositive = MergePromptParts(
				"adult male",
				"adult man",
				"male character only",
				"masculine male",
				"masculine facial structure",
				"masculine jawline",
				"flat chest",
				"male body proportions",
				"short masculine hairstyle",
				"no breasts",
				"no female",
				adjustedPositive);

			adjustedNegative = MergePromptParts(
				adjustedNegative,
				"female",
				"woman",
				"girl",
				"feminine face",
				"feminine body",
				"female body",
				"female clothing silhouette",
				"long feminine eyelashes",
				"lipstick");
		}
		else if (containsFemaleSubject && !containsMaleSubject)
		{
			adjustedPositive = MergePromptParts(
				"adult female",
				"adult woman",
				"female character only",
				"feminine facial structure",
				"female body proportions",
				"no male",
				adjustedPositive);

			adjustedNegative = MergePromptParts(
				adjustedNegative,
				"male",
				"man",
				"boy",
				"masculine face",
				"masculine jawline",
				"male body",
				"beard",
				"moustache");
		}

		if (normalizedImageMode == "expression" || (normalizedImageMode == "single" && ContainsNineGridRequest(normalizedUserPrompt)))
		{
			adjustedPositive = MergePromptParts(
				"3x3 grid",
				"nine-panel expression sheet",
				"single composite image",
				"all 9 panels visible",
				"consistent subject across all panels",
				adjustedPositive);

			adjustedNegative = MergePromptParts(
				adjustedNegative,
				"single portrait",
				"one panel only",
				"single image only",
				"cropped grid",
				"missing panels",
				"incomplete grid",
				"random collage");
		}

		if (normalizedImageMode == "threeview")
		{
			adjustedPositive = MergePromptParts(
				"character turnaround sheet",
				"three-panel layout",
				"front view",
				"true 90-degree standing side profile view in the second panel",
				"back view",
				"full body",
				"single composite image",
				"same subject across all panels",
				adjustedPositive);

			adjustedNegative = MergePromptParts(
				adjustedNegative,
				"single portrait",
				"one view only",
				"missing front view",
				"missing side view",
				"missing back view",
				"three-quarter side view",
				"front-facing side panel",
				"both eyes visible in side panel",
				"front torso visible in side panel",
				"cropped body",
				"random collage",
				"extra subject");
		}

		return (adjustedPositive, adjustedNegative);
	}

	private static string NormalizeDirectImageMode(string? imageMode)
	{
		return (imageMode ?? string.Empty).Trim().ToLowerInvariant() switch
		{
			"expression" => "expression",
			"threeview" => "threeview",
			_ => "single",
		};
	}

	private static bool ContainsDogSubject(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string normalized = text.Trim().ToLowerInvariant();
		return normalized.Contains("狗") ||
			normalized.Contains("犬") ||
			normalized.Contains("dog") ||
			normalized.Contains("canine") ||
			normalized.Contains("黑背") ||
			normalized.Contains("黑贝");
	}

	private static bool ContainsMaleSubject(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string normalized = text.Trim().ToLowerInvariant();
		return normalized.Contains("男性") ||
			normalized.Contains("男人") ||
			normalized.Contains("男生") ||
			normalized.Contains("男主") ||
			normalized.Contains("男角色") ||
			Regex.IsMatch(normalized, "\\bmale\\b|\\bman\\b|\\bboy\\b", RegexOptions.IgnoreCase);
	}

	private static bool ContainsFemaleSubject(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string normalized = text.Trim().ToLowerInvariant();
		return normalized.Contains("女性") ||
			normalized.Contains("女人") ||
			normalized.Contains("女生") ||
			normalized.Contains("女主") ||
			normalized.Contains("女角色") ||
			Regex.IsMatch(normalized, "\\bfemale\\b|\\bwoman\\b|\\bgirl\\b", RegexOptions.IgnoreCase);
	}

	private static bool ContainsNineGridRequest(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string normalized = text.Trim().ToLowerInvariant();
		return normalized.Contains("九宫格") ||
			normalized.Contains("九格") ||
			normalized.Contains("3x3") ||
			normalized.Contains("表情包") ||
			normalized.Contains("表情板") ||
			normalized.Contains("expression sheet") ||
			normalized.Contains("nine-panel") ||
			normalized.Contains("nine grid") ||
			normalized.Contains("contact sheet");
	}

	private static string MergePromptParts(params string[] parts)
	{
		return string.Join(", ", parts
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Distinct(StringComparer.OrdinalIgnoreCase));
	}

	private static string InferDirectVideoModelFamily(ModelInfo model)
	{
		string text = (model?.Id ?? string.Empty).Trim().ToLowerInvariant();
		if (text.Contains("sora"))
		{
			return "sora";
		}
		if (text.Contains("veo"))
		{
			return "veo";
		}
		if (text.Contains("grok"))
		{
			return "grok";
		}
		if (text.Contains("qwen"))
		{
			return "qwen";
		}
		if (text.Contains("runway") || text.Contains("gen3"))
		{
			return "runway";
		}
		return "luma";
	}

	private static string InferDirectVideoPlatform(ModelInfo model)
	{
		return IsYunWuLike(model.Url) ? "yunwuapi" : string.Empty;
	}

	private static void ApplyUpstreamOutlineVisualStyle(WorkflowDocument document, WorkflowNode node)
	{
		if (document == null || node == null)
		{
			return;
		}

		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		var outlineNode = WorkflowExecutor.CollectUpstreamNodes(document, node)
			.FirstOrDefault(candidate => candidate.Type == WorkflowNodeCatalog.Outline);
		outlineNode ??= document.Nodes
			.Where(candidate => candidate.Type == WorkflowNodeCatalog.Outline)
			.OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.Output) ? 0 : 1)
			.ThenBy(candidate => document.Nodes.IndexOf(candidate))
			.LastOrDefault();

		var style = outlineNode?.Params?.VisualStyle.OrDefault(string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(style) &&
			WorkflowNodeCatalog.OutlineVisualStyles.Contains(style, StringComparer.OrdinalIgnoreCase))
		{
			node.Params.VisualStyle = style;
		}
	}

	public async Task<bool> ExecuteNodeAsync(WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		if (node.Type == "本地资产")
		{
			string assetPath = node.Params?.Input ?? node.ArtifactPath ?? string.Empty;
			WorkflowExecutor.ApplyArtifactResult(node, input, assetPath, node.ArtifactKind.OrDefault("file"), "已引用本地资产。");
			return true;
		}
		if (node.Type == WorkflowNodeCatalog.TextToImage || node.Type == WorkflowNodeCatalog.ImageToImage || node.Type == WorkflowNodeCatalog.TextToVideo || node.Type == WorkflowNodeCatalog.TextImageToVideo)
		{
			return await ExecuteDirectStudioNodeAsync(node, cancellationToken);
		}
		if (node.Type == "创意描述")
		{
			return await ExecuteCreativeDescriptionNodeAsync(node, input, cancellationToken);
		}
		if (node.Type == "分镜图拆解")
		{
			return await ExecuteStoryboardBreakdownNodeAsync(document, node, input, cancellationToken);
		}
		if (node.Type == "分镜视频")
		{
			return await ExecuteStoryboardVideoNodeAsync(document, node, input, cancellationToken);
		}
		if (node.Type == "角色设计" || node.Type == "人物描述")
		{
			if (node.Params == null)
			{
				node.Params = new WorkflowNodeParameters();
			}
			node.Params.EnsureDefaults(node.Type);
			ApplyUpstreamOutlineVisualStyle(document, node);
			List<WorkflowNode> upstreamNodes = WorkflowExecutor.CollectUpstreamNodes(document, node);
			string outlineText = string.Join(Environment.NewLine + Environment.NewLine, from candidate in upstreamNodes
				where candidate.Type == "故事大纲" && !string.IsNullOrWhiteSpace(candidate.Output)
				select candidate.Output.Trim());
			string characterText = string.Join(Environment.NewLine + Environment.NewLine, from candidate in upstreamNodes
				where candidate.Type == "人物描述" && !string.IsNullOrWhiteSpace(candidate.Output)
				select candidate.Output.Trim());
			WorkflowExecutor.SyncCharacterDesignEntries(node, outlineText, characterText, 16);
			node.Output = WorkflowExecutor.BuildCharacterDesignOutput(node);
			return true;
		}
		if (node.Type == "视频合集")
		{
			await GenerateVideoCollectionAsync(document, node, input, cancellationToken);
			return true;
		}
		if (node.Type == "故事大纲")
		{
			await Task.Delay(450, cancellationToken);
		}
		ModelCategory? category = WorkflowExecutor.GetModelCategory(node.Type);
		if (!category.HasValue)
		{
			return ExecuteSimulation(document, node, input, "当前节点尚未接入真实执行，已生成模拟结果。");
		}
		ModelSettings settings = ModelConfig.Load();
		ModelInfo model = ResolveExecutionModel(settings, node, category.Value);
		if (model == null || string.IsNullOrWhiteSpace(model.Url))
		{
			bool flag = category.Value != ModelCategory.Text;
			bool flag2 = flag;
			if (flag2)
			{
				flag2 = await TryExecuteMediaTextFallbackAsync(settings, document, node, input, cancellationToken, "未配置对应媒体模型，已改为生成可直接使用的提示词或分镜文件。");
			}
			if (flag2)
			{
				return true;
			}
			if (category.Value == ModelCategory.Text)
			{
				throw new InvalidOperationException("No text model configured.请在“模型设置”里把文本节点指向 Ollama 模型。");
			}
			return ExecuteSimulation(document, node, input, "未配置可用模型，已生成模拟结果。");
		}
		try
		{
			switch (category.Value)
			{
			case ModelCategory.Text:
			{
				string result = await ExecuteTextNodeWithFallbackAsync(settings, model, node, input, cancellationToken);
				ApplyTextNodeResult(node, input, result);
				return true;
			}
			case ModelCategory.Image:
			{
				GeneratedArtifact artifact2 = await ExecuteImageNodeAsync(model, document, node, input, cancellationToken);
				WorkflowExecutor.ApplyArtifactResult(node, input, artifact2.Path, "image", artifact2.Description);
				return true;
			}
			case ModelCategory.Video:
			{
				GeneratedArtifact artifact = await ExecuteVideoNodeAsync(model, document, node, input, cancellationToken);
				WorkflowExecutor.ApplyArtifactResult(node, input, artifact.Path, "video", artifact.Description);
				return true;
			}
			}
		}
		catch (Exception ex)
		{
			if (category.Value == ModelCategory.Image)
			{
				throw new InvalidOperationException("图片模型调用失败：" + ex.Message, ex);
			}
			bool flag3 = category.Value != ModelCategory.Text;
			bool flag4 = flag3;
			if (flag4)
			{
				flag4 = await TryExecuteMediaTextFallbackAsync(settings, document, node, input, cancellationToken, "媒体模型调用失败，已改为生成文本方案：" + ex.Message);
			}
			if (flag4)
			{
				return true;
			}
			if (category.Value == ModelCategory.Text)
			{
				throw new InvalidOperationException("文本模型调用失败：" + ex.Message, ex);
			}
			return ExecuteSimulation(document, node, input, "模型调用失败，已自动回退到模拟结果。");
		}
		if (category.Value == ModelCategory.Text)
		{
			throw new InvalidOperationException("文本模型执行未返回可识别结果。");
		}
		return ExecuteSimulation(document, node, input, "执行未命中已知路径，已回退到模拟结果。");
	}

	private async Task<bool> ExecuteCreativeDescriptionNodeAsync(WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		string sourceText = WorkflowExecutor.NormalizeTextResult("创意描述", node.Output);
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			sourceText = WorkflowExecutor.NormalizeTextResult("创意描述", input);
		}
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			throw new InvalidOperationException("请先生成创意描述，再执行“拆分为影视分镜”。");
		}
		ModelSettings settings = ModelConfig.Load();
		ModelInfo model = ResolveSelectedModel(settings, node, ModelCategory.Text);
		List<StoryboardShot> shots;
		if (model == null || string.IsNullOrWhiteSpace(model.Url))
		{
			shots = WorkflowExecutor.ParseStoryboardShots(sourceText);
		}
		else
		{
			shots = WorkflowExecutor.ParseStoryboardShots(await ExecuteTextNodeAsync(model, node, sourceText, cancellationToken));
			if (shots.Count == 0)
			{
				shots = WorkflowExecutor.ParseStoryboardShots(sourceText);
			}
		}
		if (shots.Count == 0)
		{
			throw new InvalidOperationException("当前创意描述暂时无法拆分成可编辑的影视分镜，请检查内容后重试。");
		}
		node.Params.StoryboardShots = shots.Select((StoryboardShot shot) => shot.Clone()).ToList();
		node.ArtifactPath = string.Empty;
		node.ArtifactKind = string.Empty;
		return true;
	}

	private async Task<bool> ExecuteStoryboardBreakdownNodeAsync(WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		List<WorkflowNode> selectedStoryboardSources = ResolveSelectedStoryboardSources(document, node);
		if (selectedStoryboardSources.Count > 0)
		{
			await Task.Yield();
			List<StoryboardShot> splitShots = SplitStoryboardSourceNodes(node, selectedStoryboardSources);
			if (splitShots.Count == 0)
			{
				throw new InvalidOperationException("当前分镜图暂时无法切割出有效镜头，请确认上游分镜页和镜头数据已经生成。");
			}
			node.Params.StoryboardShots = splitShots;
			node.Output = WorkflowExecutor.BuildStoryboardBreakdownOutput(node);
			node.ArtifactPath = splitShots.FirstOrDefault((StoryboardShot shot) => !string.IsNullOrWhiteSpace(shot.SplitImagePath))?.SplitImagePath ?? string.Empty;
			node.ArtifactKind = "image";
			try
			{
				ProjectLibraryExportService.ExportStoryboardNodeToProjectFolder(document.ProjectName, node);
			}
			catch
			{
			}
			return true;
		}
		string sourceText = WorkflowExecutor.NormalizeTextResult("分镜图拆解", input);
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			sourceText = WorkflowExecutor.NormalizeTextResult("创意描述", input);
		}
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			sourceText = WorkflowExecutor.NormalizeTextResult("分镜图拆解", node.Output);
		}
		if (string.IsNullOrWhiteSpace(sourceText))
		{
			throw new InvalidOperationException("请先连接“分镜图片”或“创意描述”节点，再执行分镜图拆解。");
		}
		ModelSettings settings = ModelConfig.Load();
		ModelInfo model = ResolveSelectedModel(settings, node, ModelCategory.Text);
		List<StoryboardShot> shots;
		if (model == null || string.IsNullOrWhiteSpace(model.Url))
		{
			shots = WorkflowExecutor.ParseStoryboardShots(sourceText);
		}
		else
		{
			shots = WorkflowExecutor.ParseStoryboardShots(await ExecuteTextNodeAsync(model, node, sourceText, cancellationToken));
			if (shots.Count == 0)
			{
				shots = WorkflowExecutor.ParseStoryboardShots(sourceText);
			}
		}
		if (shots.Count == 0)
		{
			throw new InvalidOperationException("当前内容暂时无法拆解成可编辑分镜，请检查上游文本后重试。");
		}
		node.Params.StoryboardShots = shots.Select((StoryboardShot shot) => shot.Clone()).ToList();
		node.Output = WorkflowExecutor.BuildStoryboardBreakdownOutput(node);
		node.ArtifactPath = string.Empty;
		node.ArtifactKind = string.Empty;
		return true;
	}

	public async Task ExecuteNodeActionAsync(WorkflowDocument document, WorkflowNode node, string action, CancellationToken cancellationToken)
	{
		if (document == null || node == null || string.IsNullOrWhiteSpace(action))
		{
			return;
		}
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		if (action.StartsWith("storyboard-breakdown.refetch-shot:", StringComparison.OrdinalIgnoreCase))
		{
			RefetchStoryboardBreakdownShotImage(document, node, action.Substring("storyboard-breakdown.refetch-shot:".Length));
			return;
		}
		if (string.Equals(node.Type, "分镜视频", StringComparison.OrdinalIgnoreCase))
		{
			switch ((action ?? string.Empty).Trim().ToLowerInvariant())
			{
			case "storyboard-video.fetch-shots":
				await FetchStoryboardVideoShotsAsync(document, node, preserveSelection: true, cancellationToken);
				break;
			case "storyboard-video.back-to-selecting":
				node.Params.StoryboardVideoStage = "selecting";
				break;
			case "storyboard-video.generate-prompt":
				await FetchStoryboardVideoShotsAsync(document, node, preserveSelection: true, cancellationToken);
				await GenerateStoryboardVideoPromptAsync(document, node, autoSelectAllWhenEmpty: false, cancellationToken);
				break;
			case "storyboard-video.generate-video":
				await FetchStoryboardVideoShotsAsync(document, node, preserveSelection: true, cancellationToken);
				await GenerateStoryboardVideoPromptAsync(document, node, autoSelectAllWhenEmpty: false, cancellationToken);
				await GenerateStoryboardVideoAsync(document, node, cancellationToken);
				break;
			case "storyboard-video.fetch-video-by-task-id":
			{
				ModelInfo videoModel = ResolveStoryboardVideoExecutionModel(ModelConfig.Load(), node) ?? throw new InvalidOperationException("No video model configured.");
				if (!IsYunWuLike(videoModel.Url))
				{
					throw new InvalidOperationException("“按ID获取”目前仅支持云雾视频任务。");
				}
				GeneratedArtifact recoveredArtifact = RelocateStoryboardVideoArtifact(document, node, await RecoverYunWuStoryboardVideoAsync(videoModel, node, cancellationToken));
				WorkflowExecutor.ApplyArtifactResult(node, node.Params.StoryboardVideoPrompt.OrDefault("按任务ID回收视频"), recoveredArtifact.Path, "video", recoveredArtifact.Description);
				node.Params.StoryboardVideoStage = "completed";
				break;
			}
			}
			return;
		}
		if (string.Equals(node.Type, WorkflowNodeCatalog.VideoCollection, StringComparison.OrdinalIgnoreCase))
		{
			switch ((action ?? string.Empty).Trim().ToLowerInvariant())
			{
			case "video-collection.generate-video":
				await GenerateVideoCollectionAsync(document, node, WorkflowExecutor.CollectUpstreamOutput(document, node), cancellationToken);
				break;
			case "video-collection.extract-audio":
				await ExtractVideoCollectionAudioAsync(document, node, cancellationToken);
				break;
			}
		}
	}

	private async Task<bool> ExecuteStoryboardVideoNodeAsync(WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		await FetchStoryboardVideoShotsAsync(document, node, preserveSelection: true, cancellationToken);
		await GenerateStoryboardVideoPromptAsync(document, node, autoSelectAllWhenEmpty: true, cancellationToken);
		await GenerateStoryboardVideoAsync(document, node, cancellationToken);
		return true;
	}

	private async Task FetchStoryboardVideoShotsAsync(WorkflowDocument document, WorkflowNode node, bool preserveSelection, CancellationToken cancellationToken)
	{
		await Task.Yield();
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		List<StoryboardShot> shots = (from shot in WorkflowExecutor.CollectStoryboardShots(document, node, 96)
			select shot.Clone()).ToList();
		if (shots.Count == 0)
		{
			node.Params.StoryboardShots.Clear();
			node.Params.StoryboardVideoSelectedShotIds.Clear();
			node.Params.StoryboardVideoPrompt = string.Empty;
			node.Params.StoryboardVideoModelPrompt = string.Empty;
			node.Params.StoryboardVideoGeneratedClips.Clear();
			node.Params.StoryboardVideoFusedImagePath = string.Empty;
			node.Params.StoryboardVideoStage = "idle";
			throw new InvalidOperationException("请先连接“分镜图拆解”节点，再获取可用于视频生成的分镜镜头。");
		}
		HashSet<string> selectedIds = (preserveSelection ? new HashSet<string>((node.Params.StoryboardVideoSelectedShotIds ?? new List<string>()).Where((string id) => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		node.Params.StoryboardShots = shots;
		node.Params.StoryboardVideoSelectedShotIds = (from shot in shots
			where selectedIds.Contains(shot.Id)
			select shot.Id).ToList();
		node.Params.StoryboardVideoStage = "selecting";
	}

	private async Task GenerateStoryboardVideoPromptAsync(WorkflowDocument document, WorkflowNode node, bool autoSelectAllWhenEmpty, CancellationToken cancellationToken)
	{
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		ApplyUpstreamOutlineVisualStyle(document, node);
		if ((node.Params.StoryboardShots?.Count ?? 0) == 0)
		{
			await FetchStoryboardVideoShotsAsync(document, node, preserveSelection: true, cancellationToken);
		}
		if ((node.Params.StoryboardVideoSelectedShotIds?.Count ?? 0) == 0 && autoSelectAllWhenEmpty)
		{
			node.Params.StoryboardVideoSelectedShotIds = node.Params.StoryboardShots.Select((StoryboardShot shot) => shot.Id).ToList();
		}
		List<StoryboardShot> selectedShots = GetSelectedStoryboardVideoShots(node);
		if (selectedShots.Count == 0)
		{
			throw new InvalidOperationException("请先在右侧勾选需要输出的分镜镜头。");
		}
		node.Params.StoryboardVideoFusedImagePath = ComposeStoryboardVideoReferenceBoard(node, selectedShots);
		string upstreamInput = WorkflowExecutor.CollectUpstreamOutput(document, node);
		string displayPromptDraft = WorkflowExecutor.BuildStoryboardVideoPromptDraft(document, node, selectedShots, upstreamInput);
		string displayPrompt = displayPromptDraft;
		string modelPromptDraft = WorkflowExecutor.BuildStoryboardVideoModelPromptDraft(document, node, selectedShots, upstreamInput);
		string modelPrompt = modelPromptDraft;
		ModelSettings settings = ModelConfig.Load();
		ModelInfo textModel = ResolveStoryboardVideoPromptTextModel(settings, node);
		if (textModel != null && !string.IsNullOrWhiteSpace(textModel.Url))
		{
			try
			{
				string type = node.Type;
				displayPrompt = WorkflowExecutor.NormalizeTextResult(type, await ExecuteTextCompletionAsync(textModel, WorkflowExecutor.BuildStoryboardVideoPromptRequest(document, node, selectedShots, upstreamInput), cancellationToken, 0.35, null, "分镜视频/中文显示提示词"));
				if (string.IsNullOrWhiteSpace(displayPrompt))
				{
					displayPrompt = displayPromptDraft;
				}
			}
			catch
			{
				displayPrompt = displayPromptDraft;
			}

			try
			{
				string type = node.Type;
				modelPrompt = WorkflowExecutor.NormalizeTextResult(type, await ExecuteTextCompletionAsync(textModel, WorkflowExecutor.BuildStoryboardVideoModelPromptRequest(document, node, selectedShots, upstreamInput), cancellationToken, 0.35, null, "分镜视频/英文执行提示词"));
			}
			catch
			{
				modelPrompt = modelPromptDraft;
			}
		}
		node.Params.StoryboardVideoPrompt = displayPrompt.Trim();
		node.Params.StoryboardVideoModelPrompt = string.IsNullOrWhiteSpace(modelPrompt) ? modelPromptDraft.Trim() : modelPrompt.Trim();
		node.Params.StoryboardVideoStage = "prompting";
		node.Output = node.Params.StoryboardVideoPrompt;
		node.ArtifactKind = string.Empty;
	}

	private async Task GenerateStoryboardVideoAsync(WorkflowDocument document, WorkflowNode node, CancellationToken cancellationToken)
	{
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		if (string.IsNullOrWhiteSpace(node.Params.StoryboardVideoPrompt) || string.IsNullOrWhiteSpace(node.Params.StoryboardVideoModelPrompt))
		{
			await GenerateStoryboardVideoPromptAsync(document, node, autoSelectAllWhenEmpty: true, cancellationToken);
		}
		ModelSettings settings = ModelConfig.Load();
		ModelInfo videoModel = ResolveStoryboardVideoExecutionModel(settings, node);
		if (videoModel == null || string.IsNullOrWhiteSpace(videoModel.Url))
		{
			throw new InvalidOperationException("No video model configured.请先在模型设置里给分镜视频节点指定视频模型。");
		}
		node.Params.StoryboardVideoStage = "generating";
		List<StoryboardShot> selectedShots = GetSelectedStoryboardVideoShots(node);
		if (selectedShots.Count == 0)
		{
			throw new InvalidOperationException("请先在右侧勾选需要输出的分镜镜头。");
		}

		string upstreamInput = WorkflowExecutor.CollectUpstreamOutput(document, node);
		string originalFusedImagePath = node.Params.StoryboardVideoFusedImagePath;
		int originalDuration = node.Params.StoryboardVideoDurationSeconds;
		string originalAspectRatio = node.Params.StoryboardVideoAspectRatio;
		var generatedClips = new List<StoryboardVideoGeneratedClip>();
		GeneratedArtifact lastArtifact = default;
		try
		{
			for (int index = 0; index < selectedShots.Count; index++)
			{
				StoryboardShot shot = selectedShots[index];
				string referenceImagePath = EnsureStoryboardVideoHighDefinitionReference(node, ResolveStoryboardVideoShotReferenceImagePath(node, shot));
				int shotDurationSeconds = ResolveStoryboardVideoShotDurationSeconds(node.Params.StoryboardVideoPrompt, shot);
				node.Params.StoryboardVideoFusedImagePath = referenceImagePath;
				node.Params.StoryboardVideoDurationSeconds = shotDurationSeconds;
				node.Params.StoryboardVideoAspectRatio = GetImageAspectRatio(referenceImagePath, originalAspectRatio);
				string shotDisplayPrompt = WorkflowExecutor.BuildStoryboardVideoPromptDraft(document, node, new[] { shot }, upstreamInput);
				string shotModelPrompt = WorkflowExecutor.BuildStoryboardVideoModelPromptDraft(document, node, new[] { shot }, upstreamInput);
				try
				{
					GeneratedArtifact generatedArtifact = await ExecuteVideoTaskAsync(videoModel, node, shotModelPrompt, referenceImagePath, cancellationToken);
					lastArtifact = RelocateStoryboardVideoArtifact(document, node, generatedArtifact, Math.Max(1, shot.ShotNumber));
					var generatedClip = new StoryboardVideoGeneratedClip
					{
						ArtifactPath = lastArtifact.Path,
						ReferenceImagePath = referenceImagePath,
						ShotId = shot.Id,
						ShotNumber = Math.Max(1, shot.ShotNumber),
						Scene = shot.DisplayTitle,
						DurationSeconds = shotDurationSeconds,
						AspectRatio = node.Params.StoryboardVideoAspectRatio,
						Prompt = shotDisplayPrompt,
						ModelPrompt = shotModelPrompt,
					};
					generatedClips.Add(generatedClip);
					if (index < selectedShots.Count - 1 && ConfirmStoryboardVideoContinueAsync != null)
					{
						bool shouldContinue = await ConfirmStoryboardVideoContinueAsync(
							new StoryboardVideoClipGeneratedEventArgs(index, selectedShots.Count, generatedClip),
							cancellationToken);
						if (!shouldContinue)
						{
							break;
						}
					}
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException($"分镜视频第 {Math.Max(1, shot.ShotNumber)} 个镜头生成失败：{ex.Message}", ex);
				}
			}
		}
		finally
		{
			node.Params.StoryboardVideoFusedImagePath = originalFusedImagePath;
			node.Params.StoryboardVideoDurationSeconds = originalDuration;
			node.Params.StoryboardVideoAspectRatio = originalAspectRatio;
		}

		if (generatedClips.Count == 0 || string.IsNullOrWhiteSpace(lastArtifact.Path))
		{
			throw new InvalidOperationException("没有生成可用的视频片段。");
		}

		node.Params.StoryboardVideoGeneratedClips = generatedClips;
		WorkflowExecutor.ApplyArtifactResult(node, node.Params.StoryboardVideoPrompt, lastArtifact.Path, "video", BuildStoryboardVideoGeneratedClipsSummary(generatedClips, lastArtifact.Description));
		node.Params.StoryboardVideoStage = "completed";
	}

	private static string ResolveStoryboardVideoShotReferenceImagePath(WorkflowNode node, StoryboardShot shot)
	{
		if (!string.IsNullOrWhiteSpace(shot.SplitImagePath) && File.Exists(shot.SplitImagePath))
		{
			return shot.SplitImagePath;
		}

		return ComposeStoryboardVideoReferenceBoard(node, new[] { shot });
	}

	private static string EnsureStoryboardVideoHighDefinitionReference(WorkflowNode node, string referenceImagePath)
	{
		if (string.IsNullOrWhiteSpace(referenceImagePath) || !File.Exists(referenceImagePath))
		{
			return referenceImagePath;
		}

		try
		{
			using var source = new Bitmap(referenceImagePath);
			var portrait = source.Width <= source.Height;
			var targetWidth = portrait ? 1080 : 1920;
			var targetHeight = portrait ? 1920 : 1080;
			if (source.Width >= targetWidth && source.Height >= targetHeight)
			{
				return referenceImagePath;
			}

			using var target = new Bitmap(targetWidth, targetHeight);
			using (var graphics = Graphics.FromImage(target))
			{
				graphics.Clear(Color.Black);
				graphics.CompositingQuality = CompositingQuality.HighQuality;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				var scale = Math.Max((double)targetWidth / source.Width, (double)targetHeight / source.Height);
				var drawWidth = (int)Math.Ceiling(source.Width * scale);
				var drawHeight = (int)Math.Ceiling(source.Height * scale);
				var left = (targetWidth - drawWidth) / 2;
				var top = (targetHeight - drawHeight) / 2;
				graphics.DrawImage(source, new Rectangle(left, top, drawWidth, drawHeight));
			}

			var outputDirectory = EnsureOutputDirectory(node.Type);
			var fileName = $"{SanitizeFileSegment(Path.GetFileNameWithoutExtension(referenceImagePath))}_高清参考_{DateTime.Now:yyyyMMdd_HHmmssfff}.png";
			var outputPath = Path.Combine(outputDirectory, fileName);
			target.Save(outputPath, ImageFormat.Png);
			return outputPath;
		}
		catch
		{
			return referenceImagePath;
		}
	}

	private static int ResolveStoryboardVideoShotDurationSeconds(string displayPrompt, StoryboardShot shot)
	{
		int fallback = Math.Max(1, shot.DurationSeconds);
		if (string.IsNullOrWhiteSpace(displayPrompt))
		{
			return fallback;
		}

		int shotNumber = Math.Max(1, shot.ShotNumber);
		var match = Regex.Match(displayPrompt, $@"\[(?:Shot\s*{shotNumber}\b|第\s*{shotNumber}\s*镜)[^\]]*\]\s*(?<seconds>\d{{1,3}})\s*秒", RegexOptions.IgnoreCase);
		if (match.Success && int.TryParse(match.Groups["seconds"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
		{
			return Math.Clamp(seconds, 1, 60);
		}

		return fallback;
	}

	private static string GetImageAspectRatio(string imagePath, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
		{
			try
			{
				using Image image = Image.FromFile(imagePath);
				if (image.Width > image.Height)
				{
					return "16:9";
				}
				if (image.Height >= image.Width)
				{
					return "9:16";
				}
			}
			catch
			{
			}
		}

		return string.Equals(fallback, "9:16", StringComparison.Ordinal) ? "9:16" : "16:9";
	}

	private static string BuildStoryboardVideoGeneratedClipsSummary(IReadOnlyList<StoryboardVideoGeneratedClip> clips, string description)
	{
		var builder = new StringBuilder();
		var cleanDescription = KeepSimplifiedChineseVisibleTextOnly(description);
		builder.AppendLine(string.IsNullOrWhiteSpace(cleanDescription) ? "已按分镜顺序逐段生成视频。" : cleanDescription);
		builder.AppendLine($"已生成片段：{clips.Count}");
		for (int index = 0; index < clips.Count; index++)
		{
			StoryboardVideoGeneratedClip clip = clips[index];
			builder.AppendLine($"{index + 1}. 第{Math.Max(1, clip.ShotNumber)}镜 / {clip.DurationSeconds}秒 / 画幅{clip.AspectRatio} / {clip.ArtifactPath}");
		}
		builder.AppendLine("视频合集节点会按以上顺序读取这些片段。");
		return builder.ToString().Trim();
	}

	private static async Task<GeneratedArtifact> RecoverYunWuStoryboardVideoAsync(ModelInfo model, WorkflowNode node, CancellationToken cancellationToken)
	{
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		if (string.IsNullOrWhiteSpace(node.Params.StoryboardVideoTaskId) && string.IsNullOrWhiteSpace(node.Params.StoryboardVideoTaskQueryUrl))
		{
			throw new InvalidOperationException("当前节点还没有可回收的云端任务ID。");
		}
		GeneratedArtifact? recoveredArtifact = await TryResumeYunWuVideoTaskAsync(model, node, NormalizeYunWuRootUrl(model.Url), cancellationToken);
		if (recoveredArtifact.HasValue)
		{
			node.Params.StoryboardVideoLastError = string.Empty;
			return recoveredArtifact.Value;
		}
		string taskId = node.Params.StoryboardVideoTaskId.OrDefault("未记录");
		throw new InvalidOperationException($"未能根据当前任务ID回收视频。若云端任务已失败，则无法取回视频文件。任务ID：{taskId}");
	}

	private static List<StoryboardShot> GetSelectedStoryboardVideoShots(WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
		}
		node.Params.EnsureDefaults(node.Type);
		HashSet<string> selectedIds = new HashSet<string>((node.Params.StoryboardVideoSelectedShotIds ?? new List<string>()).Where((string id) => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
		return (from shot in node.Params.StoryboardShots ?? new List<StoryboardShot>()
			where selectedIds.Contains(shot.Id)
			select shot.Clone() into shot
			orderby Math.Max(1, shot.ShotNumber)
			select shot).ToList();
	}

	private static string ComposeStoryboardVideoReferenceBoard(WorkflowNode node, IReadOnlyList<StoryboardShot> shots)
	{
		List<StoryboardShot> list = (shots ?? Array.Empty<StoryboardShot>()).Where((StoryboardShot shot) => shot != null && !string.IsNullOrWhiteSpace(shot.SplitImagePath) && File.Exists(shot.SplitImagePath)).ToList();
		if (list.Count == 0)
		{
			return string.Empty;
		}
		using DisposableBitmapCollection disposableBitmapCollection = new DisposableBitmapCollection(list.Select((StoryboardShot shot) => shot.SplitImagePath));
		if (disposableBitmapCollection.Items.Count == 0)
		{
			return string.Empty;
		}
		int num = Math.Min(3, Math.Max(1, list.Count));
		int num2 = (int)Math.Ceiling((double)list.Count / (double)num);
		int num3 = Math.Min(256, disposableBitmapCollection.Items.Max((Bitmap bitmap2) => bitmap2.Width));
		int num4 = Math.Min(256, disposableBitmapCollection.Items.Max((Bitmap bitmap2) => bitmap2.Height));
		int width = 56 + num * num3 + Math.Max(0, num - 1) * 18;
		int height = 56 + num2 * num4 + Math.Max(0, num2 - 1) * 18;
		using Bitmap bitmap = new Bitmap(width, height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.Clear(Color.FromArgb(18, 20, 26));
		graphics.SmoothingMode = SmoothingMode.HighQuality;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
		for (int num5 = 0; num5 < disposableBitmapCollection.Items.Count && num5 < list.Count; num5++)
		{
			int num6 = num5 / num;
			int num7 = num5 % num;
			int x = 28 + num7 * (num3 + 18);
			int y = 28 + num6 * (num4 + 18);
			Rectangle targetRect = new Rectangle(x, y, num3, num4);
			DrawStoryboardVideoCell(graphics, disposableBitmapCollection.Items[num5], targetRect);
			DrawStoryboardVideoIndexBadge(graphics, targetRect, num5 + 1);
		}
		string path = EnsureOutputDirectory(node.Type);
		string text = Path.Combine(path, $"{node.Id}_storyboard_video_ref_{DateTime.Now:yyyyMMdd_HHmmss}.png");
		bitmap.Save(text, ImageFormat.Png);
		return text;
	}

	private static void DrawStoryboardVideoCell(Graphics graphics, Bitmap source, Rectangle targetRect)
	{
		Rectangle rect = FitRect(source.Size, targetRect);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(28, 30, 38));
		graphics.FillRectangle(brush, targetRect);
		graphics.DrawImage(source, rect);
		using Pen pen = new Pen(Color.FromArgb(86, 93, 118), 2f);
		graphics.DrawRectangle(pen, targetRect);
	}

	private static Rectangle FitRect(Size source, Rectangle target)
	{
		if (source.Width <= 0 || source.Height <= 0)
		{
			return target;
		}
		float num = Math.Min((float)target.Width / (float)source.Width, (float)target.Height / (float)source.Height);
		int num2 = Math.Max(1, (int)Math.Round((float)source.Width * num));
		int num3 = Math.Max(1, (int)Math.Round((float)source.Height * num));
		int x = target.X + (target.Width - num2) / 2;
		int y = target.Y + (target.Height - num3) / 2;
		return new Rectangle(x, y, num2, num3);
	}

	private static void DrawStoryboardVideoIndexBadge(Graphics graphics, Rectangle targetRect, int index)
	{
		Rectangle bounds = new Rectangle(targetRect.X + 10, targetRect.Y + 10, 34, 24);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(102, 61, 245));
		using SolidBrush brush2 = new SolidBrush(Color.White);
		using Font font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
		using GraphicsPath path = RoundedRect(bounds, 8);
		graphics.FillPath(brush, path);
		string text = $"#{index}";
		SizeF sizeF = graphics.MeasureString(text, font);
		PointF point = new PointF((float)bounds.X + ((float)bounds.Width - sizeF.Width) / 2f, (float)bounds.Y + ((float)bounds.Height - sizeF.Height) / 2f - 1f);
		graphics.DrawString(text, font, brush2, point);
	}

	private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
	{
		int num = radius * 2;
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(bounds.X, bounds.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.X, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static List<WorkflowNode> ResolveSelectedStoryboardSources(WorkflowDocument document, WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
		}
		node.Params.EnsureDefaults(node.Type);
		List<WorkflowNode> list = (from candidate in WorkflowExecutor.CollectUpstreamNodes(document, node)
			where candidate.Type == "分镜图片"
			select candidate).ToList();
		if (list.Count == 0)
		{
			return new List<WorkflowNode>();
		}
		List<string> selectedIds = (node.Params.SelectedStoryboardSourceNodeIds ?? new List<string>()).Where((string id) => !string.IsNullOrWhiteSpace(id)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		if (selectedIds.Count == 0)
		{
			return list;
		}
		List<WorkflowNode> list2 = list.Where((WorkflowNode candidate) => selectedIds.Contains<string>(candidate.Id, StringComparer.OrdinalIgnoreCase)).ToList();
		return (list2.Count > 0) ? list2 : list;
	}

	private static List<StoryboardShot> SplitStoryboardSourceNodes(WorkflowNode ownerNode, IEnumerable<WorkflowNode> sourceNodes)
	{
		List<StoryboardShot> list = new List<StoryboardShot>();
		foreach (WorkflowNode item in sourceNodes ?? Enumerable.Empty<WorkflowNode>())
		{
			WorkflowNode workflowNode = item;
			if (workflowNode.Params == null)
			{
				WorkflowNodeParameters workflowNodeParameters = (workflowNode.Params = new WorkflowNodeParameters());
			}
			item.Params.EnsureDefaults(item.Type);
			List<string> list2 = (item.Params.StoryboardGridPagePaths ?? new List<string>()).Where((string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).ToList();
			if (list2.Count == 0 && !string.IsNullOrWhiteSpace(item.ArtifactPath) && File.Exists(item.ArtifactPath))
			{
				list2.Add(item.ArtifactPath);
			}
			List<StoryboardShot> list3 = (item.Params.StoryboardShots ?? new List<StoryboardShot>()).Select((StoryboardShot shot) => shot.Clone()).ToList();
			if (list3.Count == 0)
			{
				list3 = WorkflowExecutor.ParseStoryboardShots(item.Output);
			}
			int storyboardShotsPerPage = WorkflowExecutor.GetStoryboardShotsPerPage(item.Params.StoryboardGridLayout);
			int columns = ((storyboardShotsPerPage == 6) ? 2 : 3);
			for (int num = 0; num < list2.Count; num++)
			{
				string pagePath = list2[num];
				List<StoryboardShot> list4 = list3.Skip(num * storyboardShotsPerPage).Take(storyboardShotsPerPage).ToList();
				if (list4.Count != 0)
				{
					List<string> list5 = SplitStoryboardPageImage(ownerNode, pagePath, columns, 3, list4.Count, $"{ownerNode.Id}_{item.Id}_page_{num + 1}");
					for (int num2 = 0; num2 < list4.Count && num2 < list5.Count; num2++)
					{
						StoryboardShot storyboardShot = list4[num2].Clone();
						storyboardShot.SourceNodeId = item.Id;
						storyboardShot.SourcePage = num;
						storyboardShot.PanelIndex = num2;
						storyboardShot.SplitImagePath = list5[num2];
						list.Add(storyboardShot);
					}
				}
			}
		}
		return NormalizeSplitStoryboardShots(list);
	}

	private static List<string> SplitStoryboardPageImage(WorkflowNode ownerNode, string pagePath, int columns, int rows, int panelCount, string filePrefix)
	{
		using Bitmap bitmap = new Bitmap(pagePath);
		int num = Math.Max(32, (bitmap.Width - 56 - (columns - 1) * 24) / Math.Max(1, columns));
		int num2 = Math.Max(32, (bitmap.Height - 56 - (rows - 1) * 24) / Math.Max(1, rows));
		string path = EnsureOutputDirectory(ownerNode.Type);
		List<string> list = new List<string>();
		for (int i = 0; i < panelCount; i++)
		{
			int num3 = i / columns;
			int num4 = i % columns;
			if (num3 >= rows)
			{
				break;
			}
			int num5 = 28 + num4 * (num + 24);
			int num6 = 28 + num3 * (num2 + 24);
			Rectangle srcRect = new Rectangle(num5, num6, Math.Min(num, bitmap.Width - num5), Math.Min(num2, bitmap.Height - num6));
			using Bitmap bitmap2 = new Bitmap(srcRect.Width, srcRect.Height);
			using (Graphics graphics = Graphics.FromImage(bitmap2))
			{
				graphics.Clear(Color.FromArgb(18, 20, 26));
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.DrawImage(bitmap, new Rectangle(0, 0, bitmap2.Width, bitmap2.Height), srcRect, GraphicsUnit.Pixel);
			}
			string text = Path.Combine(path, $"{SanitizeFileSegment(filePrefix)}_shot_{i + 1:D2}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
			bitmap2.Save(text, ImageFormat.Png);
			list.Add(text);
		}
		return list;
	}

	private static void RefetchStoryboardBreakdownShotImage(WorkflowDocument document, WorkflowNode node, string shotId)
	{
		if (!string.Equals(node.Type, WorkflowNodeCatalog.StoryboardBreakdown, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("只有“分镜图拆解”节点支持单个分镜重新获取。");
		}
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		node.Params.StoryboardShots ??= new List<StoryboardShot>();
		List<StoryboardShot> storyboardShots = node.Params.StoryboardShots;
		int num = storyboardShots.FindIndex((StoryboardShot shot) => string.Equals(shot.Id, shotId, StringComparison.OrdinalIgnoreCase));
		if (num < 0)
		{
			throw new InvalidOperationException("未找到需要重新获取的分镜。");
		}
		StoryboardShot storyboardShot = storyboardShots[num];
		WorkflowNode workflowNode = document.Nodes.FirstOrDefault((WorkflowNode candidate) => string.Equals(candidate.Id, storyboardShot.SourceNodeId, StringComparison.OrdinalIgnoreCase));
		if (workflowNode == null)
		{
			throw new InvalidOperationException("未找到分镜图片来源节点，无法重新获取。");
		}
		workflowNode.Params ??= new WorkflowNodeParameters();
		workflowNode.Params.EnsureDefaults(workflowNode.Type);
		string storyboardSourcePagePath = ResolveStoryboardSourcePagePath(workflowNode, storyboardShot);
		if (string.IsNullOrWhiteSpace(storyboardSourcePagePath) || !File.Exists(storyboardSourcePagePath))
		{
			throw new InvalidOperationException("未找到分镜页图片，无法重新获取。");
		}
		string splitImagePath = RecutStoryboardShotImage(node, workflowNode, storyboardShot, storyboardSourcePagePath);
		StoryboardShot storyboardShot2 = storyboardShot.Clone();
		storyboardShot2.SplitImagePath = splitImagePath;
		storyboardShots[num] = storyboardShot2;
		node.Output = WorkflowExecutor.BuildStoryboardBreakdownOutput(node);
	}

	private static string ResolveStoryboardSourcePagePath(WorkflowNode sourceNode, StoryboardShot shot)
	{
		List<string> list = (sourceNode.Params?.StoryboardGridPagePaths ?? new List<string>()).Where((string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).ToList();
		if (list.Count > 0)
		{
			int num = Math.Clamp(shot.SourcePage, 0, list.Count - 1);
			return list[num];
		}
		if (!string.IsNullOrWhiteSpace(sourceNode.ArtifactPath) && File.Exists(sourceNode.ArtifactPath))
		{
			return sourceNode.ArtifactPath;
		}
		return string.Empty;
	}

	private static string RecutStoryboardShotImage(WorkflowNode ownerNode, WorkflowNode sourceNode, StoryboardShot shot, string pagePath)
	{
		int storyboardShotsPerPage = WorkflowExecutor.GetStoryboardShotsPerPage(sourceNode.Params?.StoryboardGridLayout);
		int num = ((storyboardShotsPerPage == 6) ? 2 : 3);
		int num2 = 3;
		int num3 = Math.Max(0, shot.PanelIndex);
		using Bitmap bitmap = new Bitmap(pagePath);
		int num4 = Math.Max(32, (bitmap.Width - 56 - (num - 1) * 24) / Math.Max(1, num));
		int num5 = Math.Max(32, (bitmap.Height - 56 - (num2 - 1) * 24) / Math.Max(1, num2));
		int num6 = num3 / num;
		int num7 = num3 % num;
		if (num6 >= num2)
		{
			throw new InvalidOperationException("当前分镜序号超出了分镜页可裁切范围。");
		}
		int num8 = 28 + num7 * (num4 + 24);
		int num9 = 28 + num6 * (num5 + 24);
		Rectangle srcRect = new Rectangle(num8, num9, Math.Min(num4, bitmap.Width - num8), Math.Min(num5, bitmap.Height - num9));
		using Bitmap bitmap2 = new Bitmap(srcRect.Width, srcRect.Height);
		using (Graphics graphics = Graphics.FromImage(bitmap2))
		{
			graphics.Clear(Color.FromArgb(18, 20, 26));
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.DrawImage(bitmap, new Rectangle(0, 0, bitmap2.Width, bitmap2.Height), srcRect, GraphicsUnit.Pixel);
		}
		string path = EnsureOutputDirectory(ownerNode.Type);
		string text = Path.Combine(path, $"{SanitizeFileSegment(ownerNode.Id)}_{SanitizeFileSegment(sourceNode.Id)}_page_{shot.SourcePage + 1}_shot_{num3 + 1:D2}_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");
		bitmap2.Save(text, ImageFormat.Png);
		return text;
	}

	private static List<StoryboardShot> NormalizeSplitStoryboardShots(IEnumerable<StoryboardShot> shots)
	{
		List<StoryboardShot> list = new List<StoryboardShot>();
		int num = 0;
		foreach (StoryboardShot item in shots ?? Enumerable.Empty<StoryboardShot>())
		{
			if (item != null)
			{
				StoryboardShot storyboardShot = item.Clone();
				storyboardShot.Id = (string.IsNullOrWhiteSpace(storyboardShot.Id) ? Guid.NewGuid().ToString("N") : storyboardShot.Id.Trim());
				storyboardShot.Scene = (string.IsNullOrWhiteSpace(storyboardShot.Scene) ? $"分镜 {list.Count + 1}" : storyboardShot.Scene.Trim());
				storyboardShot.VisualDescription = (string.IsNullOrWhiteSpace(storyboardShot.VisualDescription) ? storyboardShot.Scene : storyboardShot.VisualDescription.Trim());
				storyboardShot.DurationSeconds = Math.Max(1, storyboardShot.DurationSeconds);
				storyboardShot.Dialogue = (string.IsNullOrWhiteSpace(storyboardShot.Dialogue) ? "无" : storyboardShot.Dialogue.Trim());
				storyboardShot.VisualEffects = (string.IsNullOrWhiteSpace(storyboardShot.VisualEffects) ? "无" : storyboardShot.VisualEffects.Trim());
				storyboardShot.AudioEffects = (string.IsNullOrWhiteSpace(storyboardShot.AudioEffects) ? "无" : storyboardShot.AudioEffects.Trim());
				storyboardShot.ShotSize = (StoryboardShotCatalog.ShotSizes.Contains(storyboardShot.ShotSize) ? storyboardShot.ShotSize : "中景");
				storyboardShot.CameraAngle = (StoryboardShotCatalog.CameraAngles.Contains(storyboardShot.CameraAngle) ? storyboardShot.CameraAngle : "平视");
				storyboardShot.CameraMovement = (StoryboardShotCatalog.CameraMovements.Contains(storyboardShot.CameraMovement) ? storyboardShot.CameraMovement : "固定");
				StoryboardShot storyboardShot2 = storyboardShot;
				if (storyboardShot2.Characters == null)
				{
					List<string> list2 = (storyboardShot2.Characters = new List<string>());
				}
				storyboardShot.Characters = (from value in storyboardShot.Characters
					where !string.IsNullOrWhiteSpace(value)
					select value.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
				storyboardShot.StartTime = num;
				storyboardShot.EndTime = num + storyboardShot.DurationSeconds;
				storyboardShot.ShotNumber = list.Count + 1;
				num = storyboardShot.EndTime;
				list.Add(storyboardShot);
			}
		}
		return list;
	}

	public async Task ExecuteCharacterDesignActionAsync(WorkflowDocument document, WorkflowNode node, string characterName, CharacterDesignActionType action, CancellationToken cancellationToken)
	{
		if (node.Type != "角色设计" && node.Type != "人物描述")
		{
			throw new InvalidOperationException("只有“角色视图”节点支持角色设计动作。");
		}
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		ApplyUpstreamOutlineVisualStyle(document, node);
		List<WorkflowNode> upstreamNodes = WorkflowExecutor.CollectUpstreamNodes(document, node);
		string upstreamInput = WorkflowExecutor.CollectUpstreamOutput(document, node);
		string outlineText = string.Join(Environment.NewLine + Environment.NewLine, from candidate in upstreamNodes
			where candidate.Type == "故事大纲" && !string.IsNullOrWhiteSpace(candidate.Output)
			select candidate.Output.Trim());
		string characterText = string.Join(Environment.NewLine + Environment.NewLine, from candidate in upstreamNodes
			where candidate.Type == "人物描述" && !string.IsNullOrWhiteSpace(candidate.Output)
			select candidate.Output.Trim());
		WorkflowExecutor.SyncCharacterDesignEntries(node, outlineText, characterText, 16);
		CharacterDesignEntry entry = node.Params.CharacterEntries.FirstOrDefault((CharacterDesignEntry candidate) => string.Equals(candidate.Name, characterName, StringComparison.OrdinalIgnoreCase));
		if (entry == null)
		{
			throw new InvalidOperationException("未找到角色：" + characterName);
		}
		node.Params.SelectedCharacterName = entry.Name;
		ModelSettings settings = ModelConfig.Load();
		ModelInfo? textModel = ResolveNodeImagePromptTextModel(settings, node);
		if (action == CharacterDesignActionType.GenerateProfile &&
			(textModel == null || string.IsNullOrWhiteSpace(textModel.Url)))
		{
			throw new InvalidOperationException("No text model configured.请先在“模型设置”里配置文本模型。");
		}
		ModelInfo? textToImageModel = ResolveCharacterTextToImageModel(settings, node);
		ModelInfo? imageToImageModel = ResolveCharacterImageToImageModel(settings, node);
		ModelInfo? imageModel = imageToImageModel ?? textToImageModel;
		try
		{
			entry.LastError = string.Empty;
			switch (action)
			{
			case CharacterDesignActionType.GenerateProfile:
				entry.ProfileStatus = CharacterAssetStatus.Generating;
				entry.LastError = string.Empty;
				await EnsureCharacterProfileAsync(textModel!, node, entry, upstreamInput, cancellationToken, forceRefresh: true);
				entry.ProfileStatus = CharacterAssetStatus.Success;
				break;
			case CharacterDesignActionType.GenerateExpression:
			{
				if (textModel != null)
				{
					await EnsureCharacterProfileAsync(textModel, node, entry, upstreamInput, cancellationToken);
				}
				entry.ExpressionStatus = CharacterAssetStatus.Generating;
				entry.LastError = string.Empty;
				var storedPrompt = SplitStoredCharacterPrompt(entry.ExpressionPrompt);
				storedPrompt = SanitizeStoredCharacterPrompt(storedPrompt);
				string normalizedPrompt2 = !string.IsNullOrWhiteSpace(storedPrompt.Positive)
					? storedPrompt.Positive
					: BuildCharacterExpressionIdentityFallbackPrompt(node, entry);
				normalizedPrompt2 = NormalizeCharacterImagePrompt(node, entry, normalizedPrompt2, CharacterDesignActionType.GenerateExpression);
				string negativePrompt2 = storedPrompt.Negative.OrDefault(WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry));
				string effectiveNegativePrompt2 = MergeCommaPromptFragments(WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry), !string.IsNullOrWhiteSpace(storedPrompt.Negative) ? storedPrompt.Negative : negativePrompt2, BuildExpressionIdentityNegativePrompt(entry));
				entry.ExpressionPrompt = string.IsNullOrWhiteSpace(effectiveNegativePrompt2)
					? $"Positive:{Environment.NewLine}{normalizedPrompt2}"
					: $"Positive:{Environment.NewLine}{normalizedPrompt2}{Environment.NewLine}{Environment.NewLine}Negative:{Environment.NewLine}{effectiveNegativePrompt2}";
				entry.ReferencePortraitPath = string.Empty;
				GeneratedArtifact artifact2 = await GenerateCharacterDesignArtifactAsync(node, entry, textToImageModel, imageToImageModel, normalizedPrompt2, effectiveNegativePrompt2, CharacterDesignActionType.GenerateExpression, cancellationToken);
				entry.ExpressionSheetPath = artifact2.Path;
				entry.ExpressionStatus = CharacterAssetStatus.Success;
				node.ArtifactPath = artifact2.Path;
				node.ArtifactKind = "image";
				break;
			}
			case CharacterDesignActionType.GenerateThreeView:
			{
				if (textModel != null)
				{
					await EnsureCharacterProfileAsync(textModel, node, entry, upstreamInput, cancellationToken);
				}
				if (!entry.HasExpressionSheet)
				{
					throw new InvalidOperationException("请先生成该角色的九宫格图片，再继续生成三视图。");
				}
				entry.ThreeViewStatus = CharacterAssetStatus.Generating;
				entry.LastError = string.Empty;
				var storedPrompt = SplitStoredCharacterPrompt(entry.ThreeViewPrompt);
				storedPrompt = SanitizeStoredCharacterPrompt(storedPrompt);
				string normalizedPrompt = !string.IsNullOrWhiteSpace(storedPrompt.Positive)
					? storedPrompt.Positive
					: BuildCharacterThreeViewIdentityFallbackPrompt(node, entry);
				normalizedPrompt = NormalizeCharacterImagePrompt(node, entry, normalizedPrompt, CharacterDesignActionType.GenerateThreeView);
				string negativePrompt = storedPrompt.Negative.OrDefault(WorkflowExecutor.BuildCharacterThreeViewNegativePrompt(node, entry));
				string effectiveNegativePrompt = MergeCommaPromptFragments(WorkflowExecutor.BuildCharacterThreeViewNegativePrompt(node, entry), !string.IsNullOrWhiteSpace(storedPrompt.Negative) ? storedPrompt.Negative : negativePrompt, BuildThreeViewIdentityNegativePrompt(entry));
				entry.ThreeViewPrompt = string.IsNullOrWhiteSpace(effectiveNegativePrompt)
					? $"Positive:{Environment.NewLine}{normalizedPrompt}"
					: $"Positive:{Environment.NewLine}{normalizedPrompt}{Environment.NewLine}{Environment.NewLine}Negative:{Environment.NewLine}{effectiveNegativePrompt}";
				GeneratedArtifact artifact = await GenerateCharacterDesignArtifactAsync(node, entry, textToImageModel, imageToImageModel, normalizedPrompt, effectiveNegativePrompt, CharacterDesignActionType.GenerateThreeView, cancellationToken);
				entry.ThreeViewSheetPath = artifact.Path;
				entry.ThreeViewStatus = CharacterAssetStatus.Success;
				node.ArtifactPath = artifact.Path;
				node.ArtifactKind = "image";
				break;
			}
			}
			node.Output = WorkflowExecutor.BuildCharacterDesignOutput(node);
		}
		catch (Exception ex)
		{
			entry.LastError = ex.Message;
			switch (action)
			{
			case CharacterDesignActionType.GenerateProfile:
				entry.ProfileStatus = CharacterAssetStatus.Failed;
				break;
			case CharacterDesignActionType.GenerateExpression:
				entry.ExpressionStatus = CharacterAssetStatus.Failed;
				break;
			default:
				entry.ThreeViewStatus = CharacterAssetStatus.Failed;
				break;
			}
			ModelCallLogService.LogFailure(GetCharacterDesignModuleName(action), (action == CharacterDesignActionType.GenerateProfile) ? textModel : (imageModel ?? textModel), ex.Message, null, "角色: " + entry.Name);
			node.Output = WorkflowExecutor.BuildCharacterDesignOutput(node);
			throw;
		}
	}

	private async Task GenerateVideoCollectionAsync(WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		List<VideoCollectionSourceItem> selectedSources = VideoCollectionSupport.GetTimelineSources(document, node);
		if (selectedSources.Count == 0)
		{
			throw new InvalidOperationException("请先连接分镜视频结果，并至少勾选一个视频片段。");
		}
		string editProjectPath = SaveVideoCollectionEditProject(node, selectedSources);
		node.Params.VideoCollectionEditProjectPath = editProjectPath;
		bool hasPostEffects = HasVideoCollectionPostEffects(node);
		if (selectedSources.Count == 1)
		{
			string singleSourcePath = selectedSources[0].ArtifactPath;
			string extension = Path.GetExtension(singleSourcePath);
			string outputDirectory = EnsureOutputDirectory(node.Type);
			string mergedPath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_collection{(string.IsNullOrWhiteSpace(extension) ? ".mp4" : extension)}");
			File.Copy(singleSourcePath, mergedPath, overwrite: true);
			string finalPath = mergedPath;
			bool effectsRendered = false;
			if (hasPostEffects)
			{
				string effectFfmpegPath = TryLocateFfmpeg();
				if (!string.IsNullOrWhiteSpace(effectFfmpegPath))
				{
					string editedPath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_edited.mp4");
					string? singleSubtitlePath = SaveVideoCollectionSubtitleFile(node, selectedSources);
					effectsRendered = await TryRenderVideoCollectionPostEffectsAsync(effectFfmpegPath, node, mergedPath, editedPath, singleSubtitlePath, cancellationToken);
					if (effectsRendered && File.Exists(editedPath))
					{
						finalPath = editedPath;
					}
				}
			}
			node.Params.VideoCollectionCurrentArtifactPath = finalPath;
			node.Params.VideoCollectionPlaylistPath = string.Empty;
			WorkflowExecutor.ApplyArtifactResult(node, input, finalPath, "video", BuildVideoCollectionOutput(selectedSources, finalPath, null, editProjectPath, multipleSourcesMerged: false, postEffectsRendered: effectsRendered));
			return;
		}
		string manifestPath = SaveVideoCollectionManifest(node, selectedSources);
		node.Params.VideoCollectionPlaylistPath = manifestPath;
		string ffmpegPath = TryLocateFfmpeg();
		if (string.IsNullOrWhiteSpace(ffmpegPath))
		{
			node.ArtifactPath = string.Empty;
			node.ArtifactKind = string.Empty;
			node.Output = BuildVideoCollectionOutput(selectedSources, null, manifestPath, editProjectPath, multipleSourcesMerged: false, postEffectsRendered: false);
			return;
		}
		string outputDirectory2 = EnsureOutputDirectory(node.Type);
		string mergedBasePath = Path.Combine(outputDirectory2, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_merged.mp4");
		string? subtitlePath = hasPostEffects ? SaveVideoCollectionSubtitleFile(node, selectedSources) : null;
		if (!string.Equals(WorkflowNodeParameters.NormalizeVideoCollectionTransitionType(node.Params.VideoCollectionTransitionType), "none", StringComparison.Ordinal))
		{
			string timelinePath = Path.Combine(outputDirectory2, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_timeline.mp4");
			bool timelineRendered = await TryRenderVideoCollectionTimelineAsync(ffmpegPath, node, selectedSources, timelinePath, subtitlePath, cancellationToken);
			if (timelineRendered && File.Exists(timelinePath))
			{
				string timelineOutputPath = timelinePath;
				if (HasVideoCollectionOverlays(node))
				{
					string timelineEditedPath = Path.Combine(outputDirectory2, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_timeline_edited.mp4");
					if (await TryRenderVideoCollectionPostEffectsAsync(ffmpegPath, node, timelinePath, timelineEditedPath, null, cancellationToken, includeTransition: false) &&
						File.Exists(timelineEditedPath))
					{
						timelineOutputPath = timelineEditedPath;
					}
				}

				node.Params.VideoCollectionCurrentArtifactPath = timelineOutputPath;
				WorkflowExecutor.ApplyArtifactResult(node, input, timelineOutputPath, "video", BuildVideoCollectionOutput(selectedSources, timelineOutputPath, manifestPath, editProjectPath, multipleSourcesMerged: true, postEffectsRendered: true));
				return;
			}
		}
		bool merged = await TryMergeVideosWithFfmpegAsync(ffmpegPath, manifestPath, mergedBasePath, cancellationToken);
		if (!merged || !File.Exists(mergedBasePath))
		{
			node.ArtifactPath = string.Empty;
			node.ArtifactKind = string.Empty;
			node.Output = BuildVideoCollectionOutput(selectedSources, null, manifestPath, editProjectPath, multipleSourcesMerged: false, postEffectsRendered: false);
			return;
		}
		string outputPath = mergedBasePath;
		bool postEffectsRendered = false;
		if (hasPostEffects)
		{
			string editedPath = Path.Combine(outputDirectory2, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_edited.mp4");
			postEffectsRendered = await TryRenderVideoCollectionPostEffectsAsync(ffmpegPath, node, mergedBasePath, editedPath, subtitlePath, cancellationToken);
			if (postEffectsRendered && File.Exists(editedPath))
			{
				outputPath = editedPath;
			}
		}
		node.Params.VideoCollectionCurrentArtifactPath = outputPath;
		WorkflowExecutor.ApplyArtifactResult(node, input, outputPath, "video", BuildVideoCollectionOutput(selectedSources, outputPath, manifestPath, editProjectPath, multipleSourcesMerged: true, postEffectsRendered: postEffectsRendered));
	}

	private static bool HasVideoCollectionPostEffects(WorkflowNode node)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		return (!string.IsNullOrWhiteSpace(node.Params.VideoCollectionAudioPath) && File.Exists(node.Params.VideoCollectionAudioPath)) ||
			!string.IsNullOrWhiteSpace(node.Params.VideoCollectionSubtitleText) ||
			HasVideoCollectionOverlays(node) ||
			!string.Equals(node.Params.VideoCollectionTransitionType, "none", StringComparison.Ordinal);
	}

	private static bool HasVideoCollectionOverlays(WorkflowNode node)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		return (node.Params.VideoCollectionOverlayItems ?? new List<VideoCollectionOverlayItem>())
			.Any(item =>
				(string.Equals(WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(item.Kind), "text", StringComparison.Ordinal) &&
				 !string.IsNullOrWhiteSpace(KeepSimplifiedChineseVisibleTextOnly(item.Text))) ||
				(string.Equals(WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(item.Kind), "image", StringComparison.Ordinal) &&
				 !string.IsNullOrWhiteSpace(item.ImagePath) &&
				 File.Exists(item.ImagePath)));
	}

	private async Task ExtractVideoCollectionAudioAsync(WorkflowDocument document, WorkflowNode node, CancellationToken cancellationToken)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		List<VideoCollectionSourceItem> timelineSources = VideoCollectionSupport.GetTimelineSources(document, node);
		string sourcePath = timelineSources
			.FirstOrDefault(source => string.Equals(source.ArtifactPath, node.Params.VideoCollectionCurrentArtifactPath, StringComparison.OrdinalIgnoreCase))
			?.ArtifactPath
			?? timelineSources.FirstOrDefault()?.ArtifactPath
			?? string.Empty;
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
		{
			throw new InvalidOperationException("请先在视频合集时间线中放入至少一个视频片段。");
		}

		string ffmpegPath = TryLocateFfmpeg();
		if (string.IsNullOrWhiteSpace(ffmpegPath))
		{
			throw new InvalidOperationException("当前机器未检测到 ffmpeg，暂时无法分离音频。");
		}

		string outputDirectory = EnsureOutputDirectory(node.Type);
		string outputPath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_detached_audio.m4a");
		var arguments = new[]
		{
			"-y",
			"-i",
			sourcePath,
			"-vn",
			"-c:a",
			"aac",
			"-b:a",
			"192k",
			outputPath
		};
		bool extracted = await RunFfmpegAsync(ffmpegPath, arguments, cancellationToken);
		if (!extracted || !File.Exists(outputPath))
		{
			throw new InvalidOperationException("分离音频失败：源视频可能没有音轨，或 ffmpeg 无法读取该视频。");
		}

		node.Params.VideoCollectionAudioPath = outputPath;
		node.Params.VideoCollectionAudioVolume = 1M;
		node.Output = $"# 音频分离完成{Environment.NewLine}{Environment.NewLine}源视频：{sourcePath}{Environment.NewLine}音频文件：{outputPath}{Environment.NewLine}音频已加入视频合集音频时间线。";
	}

	private static string SaveVideoCollectionEditProject(WorkflowNode node, IReadOnlyList<VideoCollectionSourceItem> sources)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		string outputDirectory = EnsureOutputDirectory(node.Type);
		string projectPath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_edit_project.json");
		var project = new
		{
			generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
			clipCount = sources.Count,
			clips = sources.Select((source, index) => new
			{
				order = index + 1,
				name = source.DisplayName,
				durationSeconds = source.DurationSeconds,
				path = source.ArtifactPath,
				thumbnail = source.ThumbnailPath,
				summary = source.Summary,
			}).ToList(),
			audioTrack = new
			{
				path = node.Params.VideoCollectionAudioPath,
				volume = node.Params.VideoCollectionAudioVolume,
			},
			importedAssets = (node.Params.VideoCollectionImportedAssets ?? new List<VideoCollectionImportedAsset>())
				.Select(asset => new
				{
					kind = asset.Kind,
					name = asset.DisplayName,
					durationSeconds = asset.DurationSeconds,
					path = asset.FilePath,
				})
				.ToList(),
			overlays = (node.Params.VideoCollectionOverlayItems ?? new List<VideoCollectionOverlayItem>())
				.Select(overlay => new
				{
					kind = WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(overlay.Kind),
					text = KeepSimplifiedChineseVisibleTextOnly(overlay.Text ?? string.Empty),
					imagePath = overlay.ImagePath,
					startSeconds = overlay.StartSeconds,
					durationSeconds = overlay.DurationSeconds,
					x = overlay.X,
					y = overlay.Y,
					widthRatio = overlay.WidthRatio,
					fontSize = overlay.FontSize,
				})
				.ToList(),
			subtitles = KeepSimplifiedChineseVisibleTextBlock(node.Params.VideoCollectionSubtitleText),
			transition = new
			{
				type = node.Params.VideoCollectionTransitionType,
				name = WorkflowNodeParameters.GetVideoCollectionTransitionDisplayName(node.Params.VideoCollectionTransitionType),
				seconds = node.Params.VideoCollectionTransitionSeconds,
			},
		};
		File.WriteAllText(projectPath, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
		return projectPath;
	}

	private static string? SaveVideoCollectionSubtitleFile(WorkflowNode node, IReadOnlyList<VideoCollectionSourceItem> sources)
	{
		if (node.Params == null || string.IsNullOrWhiteSpace(node.Params.VideoCollectionSubtitleText))
		{
			return null;
		}

		string[] lines = node.Params.VideoCollectionSubtitleText
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(KeepSimplifiedChineseVisibleTextOnly)
			.Where(line => !string.IsNullOrWhiteSpace(line))
			.ToArray();
		if (lines.Length == 0)
		{
			return null;
		}

		int totalSeconds = Math.Max(lines.Length, sources.Sum(source => Math.Max(1, source.DurationSeconds)));
		double step = Math.Max(1.0, (double)totalSeconds / lines.Length);
		StringBuilder builder = new StringBuilder();
		for (int index = 0; index < lines.Length; index++)
		{
			double start = index * step;
			double end = (index == lines.Length - 1) ? totalSeconds : Math.Min(totalSeconds, (index + 1) * step);
			builder.AppendLine((index + 1).ToString(CultureInfo.InvariantCulture));
			builder.AppendLine($"{FormatSrtTimestamp(start)} --> {FormatSrtTimestamp(Math.Max(start + 0.75, end))}");
			builder.AppendLine(lines[index]);
			builder.AppendLine();
		}

		string outputDirectory = EnsureOutputDirectory(node.Type);
		string subtitlePath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_subtitles.srt");
		File.WriteAllText(subtitlePath, builder.ToString(), Encoding.UTF8);
		return subtitlePath;
	}

	private static string FormatSrtTimestamp(double seconds)
	{
		TimeSpan value = TimeSpan.FromSeconds(Math.Max(0, seconds));
		return value.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
	}

	private static string SaveVideoCollectionManifest(WorkflowNode node, IReadOnlyList<VideoCollectionSourceItem> sources)
	{
		string outputDirectory = EnsureOutputDirectory(node.Type);
		string manifestPath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_concat.txt");
		StringBuilder builder = new StringBuilder();
		foreach (VideoCollectionSourceItem source in sources)
		{
			string normalizedPath = Path.GetFullPath(source.ArtifactPath).Replace("\\", "/", StringComparison.Ordinal);
			builder.AppendLine($"file '{normalizedPath.Replace("'", "'\\''", StringComparison.Ordinal)}'");
		}
		File.WriteAllText(manifestPath, builder.ToString(), Encoding.UTF8);
		return manifestPath;
	}

	private static async Task<bool> TryMergeVideosWithFfmpegAsync(string ffmpegPath, string manifestPath, string outputPath, CancellationToken cancellationToken)
	{
		bool merged = await RunFfmpegAsync(ffmpegPath, $"-y -f concat -safe 0 -i \"{manifestPath}\" -c copy \"{outputPath}\"", cancellationToken);
		if (merged && File.Exists(outputPath))
		{
			return true;
		}
		return await RunFfmpegAsync(ffmpegPath, $"-y -f concat -safe 0 -i \"{manifestPath}\" -c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p -c:a aac -b:a 192k \"{outputPath}\"", cancellationToken) && File.Exists(outputPath);
	}

	private static async Task<bool> TryRenderVideoCollectionTimelineAsync(
		string ffmpegPath,
		WorkflowNode node,
		IReadOnlyList<VideoCollectionSourceItem> sources,
		string outputPath,
		string? subtitlePath,
		CancellationToken cancellationToken)
	{
		if (node.Params == null || sources.Count < 2)
		{
			return false;
		}

		string filterGraph = BuildVideoCollectionTimelineFilter(node, sources, subtitlePath);
		if (string.IsNullOrWhiteSpace(filterGraph))
		{
			return false;
		}

		var arguments = new List<string> { "-y" };
		foreach (VideoCollectionSourceItem source in sources)
		{
			if (string.IsNullOrWhiteSpace(source.ArtifactPath) || !File.Exists(source.ArtifactPath))
			{
				return false;
			}

			arguments.AddRange(new[] { "-i", source.ArtifactPath });
		}

		bool hasAudioTrack = !string.IsNullOrWhiteSpace(node.Params.VideoCollectionAudioPath) && File.Exists(node.Params.VideoCollectionAudioPath);
		if (hasAudioTrack)
		{
			arguments.AddRange(new[] { "-stream_loop", "-1", "-i", node.Params.VideoCollectionAudioPath });
		}

		arguments.AddRange(new[] { "-filter_complex", filterGraph, "-map", "[vout]" });
		if (hasAudioTrack)
		{
			arguments.AddRange(new[] { "-map", "[aout]", "-shortest" });
		}
		else
		{
			arguments.Add("-an");
		}

		arguments.AddRange(new[] { "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p" });
		if (hasAudioTrack)
		{
			arguments.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
		}

		arguments.AddRange(new[] { "-movflags", "+faststart", outputPath });
		return await RunFfmpegAsync(ffmpegPath, arguments, cancellationToken) && File.Exists(outputPath);
	}

	private static string BuildVideoCollectionTimelineFilter(
		WorkflowNode node,
		IReadOnlyList<VideoCollectionSourceItem> sources,
		string? subtitlePath)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		string transitionType = WorkflowNodeParameters.NormalizeVideoCollectionTransitionType(node.Params.VideoCollectionTransitionType);
		if (sources.Count < 2 || string.Equals(transitionType, "none", StringComparison.Ordinal))
		{
			return string.Empty;
		}

		var filters = new List<string>();
		(int canvasWidth, int canvasHeight) = GetVideoCollectionCanvasSize(sources);
		for (int index = 0; index < sources.Count; index++)
		{
			filters.Add($"[{index}:v]scale={canvasWidth}:{canvasHeight}:force_original_aspect_ratio=decrease,pad={canvasWidth}:{canvasHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1,setpts=PTS-STARTPTS,fps=30,format=yuv420p[v{index}]");
		}

		double shortestClipSeconds = Math.Max(1, sources.Min(source => Math.Max(1, source.DurationSeconds)));
		double requestedDuration = decimal.ToDouble(Math.Clamp(node.Params.VideoCollectionTransitionSeconds, 0.2M, 2M));
		double transitionSeconds = Math.Clamp(requestedDuration, 0.2, Math.Max(0.2, Math.Min(2.0, shortestClipSeconds / 2.0)));
		string transitionDurationText = transitionSeconds.ToString("0.###", CultureInfo.InvariantCulture);
		string transitionName = GetVideoCollectionXfadeTransitionName(transitionType);
		bool hasSubtitles = !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath);

		string lastLabel = "v0";
		double runningSeconds = Math.Max(transitionSeconds + 0.1, sources[0].DurationSeconds);
		for (int index = 1; index < sources.Count; index++)
		{
			double offsetSeconds = Math.Max(0.1, runningSeconds - transitionSeconds);
			string nextLabel = index == sources.Count - 1 && !hasSubtitles ? "vout" : $"vx{index}";
			filters.Add($"[{lastLabel}][v{index}]xfade=transition={transitionName}:duration={transitionDurationText}:offset={offsetSeconds.ToString("0.###", CultureInfo.InvariantCulture)}[{nextLabel}]");
			lastLabel = nextLabel;
			runningSeconds = Math.Max(transitionSeconds + 0.1, runningSeconds + Math.Max(1, sources[index].DurationSeconds) - transitionSeconds);
		}

		if (hasSubtitles)
		{
			filters.Add($"[{lastLabel}]{BuildFfmpegSubtitleFilter(subtitlePath!)}[vout]");
		}

		bool hasAudioTrack = !string.IsNullOrWhiteSpace(node.Params.VideoCollectionAudioPath) && File.Exists(node.Params.VideoCollectionAudioPath);
		if (hasAudioTrack)
		{
			filters.Add($"[{sources.Count}:a]volume={node.Params.VideoCollectionAudioVolume.ToString("0.##", CultureInfo.InvariantCulture)}[aout]");
		}

		return string.Join(";", filters);
	}

	private static (int Width, int Height) GetVideoCollectionCanvasSize(IReadOnlyList<VideoCollectionSourceItem> sources)
	{
		foreach (VideoCollectionSourceItem source in sources)
		{
			if (string.IsNullOrWhiteSpace(source.ThumbnailPath) || !File.Exists(source.ThumbnailPath))
			{
				continue;
			}

			try
			{
				using Image image = Image.FromFile(source.ThumbnailPath);
				return image.Width <= image.Height ? (720, 1280) : (1280, 720);
			}
			catch
			{
			}
		}

		return (1280, 720);
	}

	private static string GetVideoCollectionXfadeTransitionName(string transitionType)
	{
		return transitionType switch
		{
			"black" => "fadeblack",
			"flash" => "fadewhite",
			_ => "fade",
		};
	}

	private static async Task<bool> TryRenderVideoCollectionPostEffectsAsync(
		string ffmpegPath,
		WorkflowNode node,
		string inputPath,
		string outputPath,
		string? subtitlePath,
		CancellationToken cancellationToken,
		bool includeTransition = true)
	{
		if (node.Params == null || string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
		{
			return false;
		}

		var arguments = new List<string>
		{
			"-y",
			"-i",
			inputPath,
		};

		bool hasAudioTrack = !string.IsNullOrWhiteSpace(node.Params.VideoCollectionAudioPath) && File.Exists(node.Params.VideoCollectionAudioPath);
		if (hasAudioTrack)
		{
			arguments.AddRange(new[] { "-stream_loop", "-1", "-i", node.Params.VideoCollectionAudioPath });
		}

		var imageOverlays = GetValidVideoCollectionImageOverlays(node);
		int imageInputStartIndex = hasAudioTrack ? 2 : 1;
		foreach (var overlay in imageOverlays)
		{
			arguments.AddRange(new[] { "-loop", "1", "-i", overlay.ImagePath });
		}

		if (imageOverlays.Count > 0)
		{
			string complexFilter = BuildVideoCollectionComplexFilter(node, subtitlePath, imageOverlays, imageInputStartIndex, includeTransition);
			if (string.IsNullOrWhiteSpace(complexFilter))
			{
				return false;
			}

			arguments.AddRange(new[] { "-filter_complex", complexFilter, "-map", "[vout]" });
		}
		else
		{
			string videoFilter = BuildVideoCollectionVideoFilter(node, subtitlePath, includeTransition);
			if (!string.IsNullOrWhiteSpace(videoFilter))
			{
				arguments.AddRange(new[] { "-vf", videoFilter });
			}

			arguments.AddRange(new[] { "-map", "0:v:0" });
		}

		if (hasAudioTrack)
		{
			arguments.AddRange(new[] { "-map", "1:a:0", "-filter:a", $"volume={node.Params.VideoCollectionAudioVolume.ToString("0.##", CultureInfo.InvariantCulture)}", "-shortest" });
		}
		else
		{
			arguments.AddRange(new[] { "-map", "0:a?" });
		}

		arguments.AddRange(new[] { "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", outputPath });
		return await RunFfmpegAsync(ffmpegPath, arguments, cancellationToken) && File.Exists(outputPath);
	}

	private static string BuildVideoCollectionVideoFilter(WorkflowNode node, string? subtitlePath, bool includeTransition = true)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		var filters = new List<string>();
		string transitionType = WorkflowNodeParameters.NormalizeVideoCollectionTransitionType(node.Params.VideoCollectionTransitionType);
		if (includeTransition && !string.Equals(transitionType, "none", StringComparison.Ordinal))
		{
			double duration = decimal.ToDouble(Math.Clamp(node.Params.VideoCollectionTransitionSeconds, 0.2M, 2M));
			if (string.Equals(transitionType, "flash", StringComparison.Ordinal))
			{
				filters.Add($"fade=t=in:st=0:d={duration.ToString("0.###", CultureInfo.InvariantCulture)}:color=white");
			}
			else
			{
				filters.Add($"fade=t=in:st=0:d={duration.ToString("0.###", CultureInfo.InvariantCulture)}");
			}
		}

		if (!string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath))
		{
			filters.Add(BuildFfmpegSubtitleFilter(subtitlePath));
		}

		foreach (var overlay in GetValidVideoCollectionTextOverlays(node))
		{
			filters.Add(BuildFfmpegDrawTextFilter(overlay));
		}

		return string.Join(",", filters);
	}

	private static string BuildVideoCollectionComplexFilter(
		WorkflowNode node,
		string? subtitlePath,
		IReadOnlyList<VideoCollectionOverlayItem> imageOverlays,
		int imageInputStartIndex,
		bool includeTransition)
	{
		var filters = new List<string>();
		string currentVideo = "0:v";
		int labelIndex = 0;
		string simpleVideoFilter = BuildVideoCollectionVideoFilter(node, subtitlePath, includeTransition);
		if (!string.IsNullOrWhiteSpace(simpleVideoFilter))
		{
			string nextLabel = $"vc{labelIndex++}";
			filters.Add($"[{currentVideo}]{simpleVideoFilter}[{nextLabel}]");
			currentVideo = nextLabel;
		}

		for (int index = 0; index < imageOverlays.Count; index++)
		{
			var overlay = imageOverlays[index];
			string imageLabel = $"ov{index}";
			string nextLabel = index == imageOverlays.Count - 1 ? "vout" : $"vc{labelIndex++}";
			double width = Math.Clamp(decimal.ToDouble(overlay.WidthRatio <= 0M ? 0.28M : overlay.WidthRatio), 0.05, 0.9);
			int imageWidth = Math.Clamp((int)Math.Round(1280 * width), 64, 1024);
			double x = Math.Clamp(decimal.ToDouble(overlay.X), 0.0, 1.0);
			double y = Math.Clamp(decimal.ToDouble(overlay.Y), 0.0, 1.0);
			double start = Math.Max(0.0, decimal.ToDouble(overlay.StartSeconds));
			double end = Math.Max(start + 0.2, start + decimal.ToDouble(overlay.DurationSeconds));

			filters.Add($"[{imageInputStartIndex + index}:v]scale={imageWidth}:-1,format=rgba[{imageLabel}]");
			filters.Add($"[{currentVideo}][{imageLabel}]overlay=x=(W-w)*{x.ToString("0.###", CultureInfo.InvariantCulture)}:y=(H-h)*{y.ToString("0.###", CultureInfo.InvariantCulture)}:enable='between(t,{start.ToString("0.###", CultureInfo.InvariantCulture)},{end.ToString("0.###", CultureInfo.InvariantCulture)})'[{nextLabel}]");
			currentVideo = nextLabel;
		}

		if (!string.Equals(currentVideo, "vout", StringComparison.Ordinal))
		{
			filters.Add($"[{currentVideo}]copy[vout]");
		}

		return string.Join(";", filters);
	}

	private static List<VideoCollectionOverlayItem> GetValidVideoCollectionTextOverlays(WorkflowNode node)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		return (node.Params.VideoCollectionOverlayItems ?? new List<VideoCollectionOverlayItem>())
			.Where(item => string.Equals(WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(item.Kind), "text", StringComparison.Ordinal))
			.Select(item =>
			{
				item.Text = KeepSimplifiedChineseVisibleTextOnly(item.Text ?? string.Empty);
				return item;
			})
			.Where(item => !string.IsNullOrWhiteSpace(item.Text))
			.ToList();
	}

	private static List<VideoCollectionOverlayItem> GetValidVideoCollectionImageOverlays(WorkflowNode node)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		return (node.Params.VideoCollectionOverlayItems ?? new List<VideoCollectionOverlayItem>())
			.Where(item =>
				string.Equals(WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(item.Kind), "image", StringComparison.Ordinal) &&
				!string.IsNullOrWhiteSpace(item.ImagePath) &&
				File.Exists(item.ImagePath))
			.ToList();
	}

	private static string BuildFfmpegDrawTextFilter(VideoCollectionOverlayItem overlay)
	{
		string text = EscapeFfmpegDrawText(KeepSimplifiedChineseVisibleTextOnly(overlay.Text ?? string.Empty));
		double x = Math.Clamp(decimal.ToDouble(overlay.X), 0.0, 1.0);
		double y = Math.Clamp(decimal.ToDouble(overlay.Y), 0.0, 1.0);
		double start = Math.Max(0.0, decimal.ToDouble(overlay.StartSeconds));
		double end = Math.Max(start + 0.2, start + decimal.ToDouble(overlay.DurationSeconds));
		int fontSize = Math.Clamp(overlay.FontSize <= 0 ? 44 : overlay.FontSize, 14, 160);
		string fontPath = GetFfmpegChineseFontPath();
		string fontPart = string.IsNullOrWhiteSpace(fontPath) ? string.Empty : $"fontfile='{EscapeFfmpegFilterPath(fontPath)}':";
		return $"drawtext={fontPart}text='{text}':fontcolor=white:fontsize={fontSize}:box=1:boxcolor=black@0.45:boxborderw=12:x=(w-text_w)*{x.ToString("0.###", CultureInfo.InvariantCulture)}:y=(h-text_h)*{y.ToString("0.###", CultureInfo.InvariantCulture)}:enable='between(t,{start.ToString("0.###", CultureInfo.InvariantCulture)},{end.ToString("0.###", CultureInfo.InvariantCulture)})'";
	}

	private static string GetFfmpegChineseFontPath()
	{
		string[] candidates =
		{
			@"C:\Windows\Fonts\msyh.ttc",
			@"C:\Windows\Fonts\simhei.ttf",
			@"C:\Windows\Fonts\simsun.ttc",
		};
		return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
	}

	private static string EscapeFfmpegDrawText(string text)
	{
		return text
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace(":", "\\:", StringComparison.Ordinal)
			.Replace("'", "\\'", StringComparison.Ordinal)
			.Replace(",", "\\,", StringComparison.Ordinal)
			.Replace("%", "\\%", StringComparison.Ordinal)
			.Replace("\r", string.Empty, StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal);
	}

	private static string EscapeFfmpegFilterPath(string path)
	{
		return Path.GetFullPath(path)
			.Replace("\\", "/", StringComparison.Ordinal)
			.Replace(":", "\\:", StringComparison.Ordinal)
			.Replace("'", "\\'", StringComparison.Ordinal);
	}

	private static string BuildFfmpegSubtitleFilter(string subtitlePath)
	{
		const string forceStyle = "FontName=Microsoft YaHei,FontSize=28,PrimaryColour=&H00FFFFFF,OutlineColour=&H80000000,BorderStyle=1,Outline=2,Shadow=0,Alignment=2,MarginV=36";
		return $"subtitles='{EscapeFfmpegFilterPath(subtitlePath)}':charenc=UTF-8:force_style='{forceStyle}'";
	}

	private static async Task<bool> RunFfmpegAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
	{
		try
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = ffmpegPath,
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
				},
			};
			process.Start();
			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> RunFfmpegAsync(string ffmpegPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
	{
		try
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = ffmpegPath,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardError = true,
					RedirectStandardOutput = true,
				},
			};
			foreach (string argument in arguments)
			{
				process.StartInfo.ArgumentList.Add(argument);
			}
			process.Start();
			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static string TryLocateFfmpeg()
	{
		List<string> candidates = new List<string>
		{
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe"),
		};
		foreach (string candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		try
		{
			string winGetPackagesRoot = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Microsoft",
				"WinGet",
				"Packages");
			if (Directory.Exists(winGetPackagesRoot))
			{
				string packageFfmpeg = Directory
					.EnumerateFiles(winGetPackagesRoot, "ffmpeg.exe", SearchOption.AllDirectories)
					.FirstOrDefault(path => path.IndexOf("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase) >= 0)
					?? Directory.EnumerateFiles(winGetPackagesRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault()
					?? string.Empty;
				if (!string.IsNullOrWhiteSpace(packageFfmpeg) && File.Exists(packageFfmpeg))
				{
					return packageFfmpeg;
				}
			}
		}
		catch
		{
		}
		try
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = "where.exe",
					Arguments = "ffmpeg",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				},
			};
			process.Start();
			string output = process.StandardOutput.ReadToEnd();
			process.WaitForExit(2000);
			string located = output
				.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim())
				.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && File.Exists(line)) ?? string.Empty;
			return located;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string BuildVideoCollectionOutput(
		IReadOnlyList<VideoCollectionSourceItem> sources,
		string? mergedArtifactPath,
		string? manifestPath,
		string? editProjectPath,
		bool multipleSourcesMerged,
		bool postEffectsRendered)
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("# 视频合集结果");
		builder.AppendLine();
		builder.AppendLine($"已选片段：{sources.Count}");
		if (!string.IsNullOrWhiteSpace(mergedArtifactPath))
		{
			builder.AppendLine($"合集文件：{mergedArtifactPath}");
			builder.AppendLine(multipleSourcesMerged ? "状态：已完成多段合并，可直接播放。" : "状态：当前只选中一段视频，已直接输出为合集结果。");
		}
		else
		{
			builder.AppendLine("状态：已生成合集清单，但当前机器未检测到 ffmpeg，暂时无法自动合并多段视频。");
		}
		if (!string.IsNullOrWhiteSpace(manifestPath))
		{
			builder.AppendLine($"合集清单：{manifestPath}");
		}
		if (!string.IsNullOrWhiteSpace(editProjectPath))
		{
			builder.AppendLine($"剪辑工程：{editProjectPath}");
		}
		if (postEffectsRendered)
		{
			builder.AppendLine("剪辑效果：已渲染音轨 / 字幕 / 文字图片 / 简单转场。");
		}
		builder.AppendLine();
		builder.AppendLine("片段顺序：");
		for (int index = 0; index < sources.Count; index++)
		{
			VideoCollectionSourceItem source = sources[index];
			builder.AppendLine($"{index + 1}. {source.DisplayName} -> {source.ArtifactPath}");
		}
		return builder.ToString().Trim();
	}

	private static bool ExecuteSimulation(WorkflowDocument document, WorkflowNode node, string input, string reason)
	{
		switch (node.Type)
		{
		case "故事大纲":
			WorkflowExecutor.ApplyResult(node, input, ApplyOutlineVariation(BuildStructuredOutlineSimulation(node)));
			return true;
		case "故事剧本":
			WorkflowExecutor.ApplyResult(node, input, BuildStructuredScriptSimulation(document, node, input));
			return true;
		case "人物描述":
			WorkflowExecutor.ApplyResult(node, input, BuildStructuredCharacterSimulation(document, node));
			return true;
		case "分镜图拆解":
			WorkflowExecutor.ApplyResult(node, input, BuildStructuredStoryboardBreakdownSimulation(document, node));
			return true;
		case "角色设计":
			return ExecuteSimulatedImageNode(document, node, input, "角色视图模拟图", BuildSimulatedCharacterViewSummary(document, node), Color.FromArgb(255, 122, 0), reason);
		case "分镜图片":
			return ExecuteSimulatedImageNode(document, node, input, "分镜图片模拟图", BuildSimulatedStoryboardImageSummary(document, node), Color.FromArgb(34, 211, 238), reason);
		case "分镜视频":
			return ExecuteSimulatedVideoNode(node, input, reason);
		case "视频合集":
			return ExecuteSimulatedCollectionNode(document, node, input, reason);
		default:
			WorkflowExecutor.ExecuteMockNode(node, input);
			return true;
		}
	}

	private static string ApplyOutlineVariation(string outline)
	{
		if (string.IsNullOrWhiteSpace(outline))
		{
			return outline;
		}
		string newValue = PickOne<string>("沈知微", "顾晚舟", "林昭宁", "许星遥");
		string newValue2 = PickOne<string>("程砚舟", "裴照临", "陆沉川", "周既明");
		string newValue3 = PickOne<string>("闻叙白", "纪临渊", "贺沉砚", "秦照野");
		string newValue4 = PickOne<string>("林妙", "唐岁禾", "乔未晞", "孟知夏");
		string newValue5 = PickOne<string>("周叔", "韩叔", "罗叔", "秦叔");
		string newValue6 = PickOne<string>("宋临", "祁野", "顾澄", "陆启安");
		string newValue7 = PickOne<string>("许未央", "贺霜", "陈惊鹤", "沈烈");
		string newValue8 = PickOne<string>("旧案编号匣", "封存卷宗箱", "密钥档案盒");
		string newValue9 = PickOne<string>("匿名终端", "折叠情报机", "幽灵通讯器");
		string newValue10 = PickOne<string>("倒计时凭证", "限时契约卡", "红线通行签");
		string newValue11 = PickOne<string>("通行卡", "夜间门禁牌", "临时通行签");
		string newValue12 = PickOne<string>("录音笔", "口袋录音器", "微型声纹笔");
		string newValue13 = PickOne<string>("备用手机", "匿名备机", "隐线通讯机");
		string newValue14 = PickOne<string>("夜班工牌", "值班胸卡", "深夜巡场证");
		string newValue15 = PickOne<string>("监控截图", "失真监控帧", "异常抓拍图");
		string newValue16 = PickOne<string>("城市地图", "地下路线图", "旧城区布防图");
		string[] array = PickOne(new string[5] { "命运开局", "关系失衡", "真相逼近", "正面反击", "终局落点" }, new string[5] { "误入深水", "信任裂缝", "暗线浮出", "局势反咬", "余波定局" }, new string[5] { "风暴前夜", "盟友失真", "秘密穿孔", "反手破局", "落幕回响" });
		string newValue17 = PickOne<string>("高密度冲突、强情绪反转与持续悬念", "强反差身份、情感拉扯与连续高能钩子", "高压困局、暧昧博弈与层层升级的反转");
		string text = PickOne<string>("情感拉扯更强", "悬疑感更重", "身份反差更突出", "群像关系更丰富");
		string text2 = outline.Replace("沈知微", newValue, StringComparison.Ordinal).Replace("程砚舟", newValue2, StringComparison.Ordinal).Replace("闻叙白", newValue3, StringComparison.Ordinal)
			.Replace("林妙", newValue4, StringComparison.Ordinal)
			.Replace("周叔", newValue5, StringComparison.Ordinal)
			.Replace("宋临", newValue6, StringComparison.Ordinal)
			.Replace("许未央", newValue7, StringComparison.Ordinal)
			.Replace("旧案编号匣", newValue8, StringComparison.Ordinal)
			.Replace("匿名终端", newValue9, StringComparison.Ordinal)
			.Replace("倒计时凭证", newValue10, StringComparison.Ordinal)
			.Replace("通行卡", newValue11, StringComparison.Ordinal)
			.Replace("录音笔", newValue12, StringComparison.Ordinal)
			.Replace("备用手机", newValue13, StringComparison.Ordinal)
			.Replace("夜班工牌", newValue14, StringComparison.Ordinal)
			.Replace("监控截图", newValue15, StringComparison.Ordinal)
			.Replace("城市地图", newValue16, StringComparison.Ordinal)
			.Replace("命运开局", array[0], StringComparison.Ordinal)
			.Replace("关系失衡", array[1], StringComparison.Ordinal)
			.Replace("真相逼近", array[2], StringComparison.Ordinal)
			.Replace("正面反击", array[3], StringComparison.Ordinal)
			.Replace("终局落点", array[4], StringComparison.Ordinal)
			.Replace("高密度冲突、强情绪反转与持续悬念", newValue17, StringComparison.Ordinal);
		return text2.Replace("**视觉风格**:", "**本次版本侧重**: " + text + Environment.NewLine + "**视觉风格**:", StringComparison.Ordinal);
	}

	private static T PickOne<T>(params T[] values)
	{
		if (values == null || values.Length == 0)
		{
			throw new ArgumentException("PickOne requires at least one value.", "values");
		}
		return values[Random.Shared.Next(values.Length)];
	}

	private static string BuildSimulatedOutline(WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
			WorkflowNodeParameters workflowNodeParameters3 = workflowNodeParameters;
		}
		node.Params.EnsureDefaults(node.Type);
		string text = (string.IsNullOrWhiteSpace(node.Params.CoreIdea) ? "一个关于逆境翻盘与情感救赎的短剧故事" : node.Params.CoreIdea.Trim());
		string text2 = (string.IsNullOrWhiteSpace(node.Params.Genre) ? "都市 (Urban)" : node.Params.Genre);
		string value = (string.IsNullOrWhiteSpace(node.Params.Setting) ? "现代都市 (Modern City)" : node.Params.Setting);
		string visualStyleDisplayName = WorkflowNodeParameters.GetVisualStyleDisplayName(node.Params.VisualStyle);
		int num = Math.Max(1, node.Params.Episodes);
		decimal value2 = ((node.Params.DurationMinutes <= 0m) ? 1m : node.Params.DurationMinutes);
		string value3 = GuessDramaTitle(text, text2);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("标题：《");
		handler.AppendFormatted(value3);
		handler.AppendLiteral("》");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(29, 3, stringBuilder2);
		handler.AppendLiteral("一句话卖点：");
		handler.AppendFormatted(text);
		handler.AppendLiteral("，在“");
		handler.AppendFormatted(text2);
		handler.AppendLiteral(" / ");
		handler.AppendFormatted(value);
		handler.AppendLiteral("”框架下走强冲突、强情绪、强反转。");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 3, stringBuilder2);
		handler.AppendLiteral("视觉定位：");
		handler.AppendFormatted(visualStyleDisplayName);
		handler.AppendLiteral(" 风格，单集 ");
		handler.AppendFormatted(value2, "0.#");
		handler.AppendLiteral(" 分钟，总计 ");
		handler.AppendFormatted(num);
		handler.AppendLiteral(" 集。");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("核心人物：");
		stringBuilder.AppendLine("1. 女主：表面克制理智，内心有执念，负责情绪共鸣和成长线。");
		stringBuilder.AppendLine("2. 男主：高压强势但有隐情，负责冲突和反转。");
		stringBuilder.AppendLine("3. 对立角色：推动误会、资源争夺或身份反转。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("主线冲突：");
		stringBuilder.AppendLine("女主在关键事件中与男主命运绑定，从互相试探到被迫合作，再到情感撕裂和真相揭露。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("分集规划：");
		foreach (string item in BuildEpisodePlans(num, text))
		{
			stringBuilder.AppendLine(item);
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("情绪钩子：身份落差、误会升级、公开对峙、被迫联手、真相反转。");
		return stringBuilder.ToString().Trim();
	}

	private static string BuildSimulatedScript(WorkflowDocument document, WorkflowNode node, string input)
	{
		WorkflowNode workflowNode = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "故事大纲");
		int val = Math.Max(1, workflowNode?.Params?.Episodes ?? 6);
		decimal value = workflowNode?.Params?.DurationMinutes ?? 1m;
		string value2 = GuessDramaTitle(workflowNode?.Params?.CoreIdea ?? input, workflowNode?.Params?.Genre);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
		handler.AppendLiteral("剧本标题：《");
		handler.AppendFormatted(value2);
		handler.AppendLiteral("》");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(50, 1, stringBuilder2);
		handler.AppendLiteral("节奏说明：短剧节奏，单集 ");
		handler.AppendFormatted(value, "0.#");
		handler.AppendLiteral(" 分钟，采用“开场钩子 - 冲突升级 - 情绪反转 - 结尾留扣”的结构。");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine();
		foreach (int item in Enumerable.Range(1, Math.Min(val, 12)))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder2);
			handler.AppendLiteral("第 ");
			handler.AppendFormatted(item);
			handler.AppendLiteral(" 集");
			stringBuilder5.AppendLine(ref handler);
			stringBuilder.AppendLine("场景 1：开场冲突。女主在公开场合遭遇压迫或误解，男主首次强势介入。");
			stringBuilder.AppendLine("场景 2：关系推进。双方因共同目标暂时合作，情绪张力拉满。");
			stringBuilder.AppendLine("场景 3：悬念结尾。以新线索、新敌人或身份反转作结。");
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString().Trim();
	}

	private static string BuildSimulatedCharacterDescriptions(WorkflowDocument document, WorkflowNode node)
	{
		string visualStyleDisplayName = WorkflowNodeParameters.GetVisualStyleDisplayName(WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "故事大纲")?.Params?.VisualStyle ?? "ANIME");
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("角色设定卡");
		stringBuilder.AppendLine("1. 女主");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
		handler.AppendLiteral("外形：五官清冷、轮廓利落，服装风格统一为 ");
		handler.AppendFormatted(visualStyleDisplayName);
		handler.AppendLiteral(" 质感。");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine("性格：嘴硬心软，行动力强，遇强则强。");
		stringBuilder.AppendLine("动机：证明自己、守护家人或夺回重要机会。");
		stringBuilder.AppendLine("视觉标签：利落长发、简洁配色、情绪强时眼神锋利。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("2. 男主");
		stringBuilder.AppendLine("外形：高辨识度轮廓，穿搭克制高级，镜头里有压迫感。");
		stringBuilder.AppendLine("性格：寡言、强势、执行力极高，但在关键时刻会失控护人。");
		stringBuilder.AppendLine("动机：维持秩序、隐藏秘密、保护核心关系。");
		stringBuilder.AppendLine("视觉标签：深色系服装、稳定站姿、情绪变化更多体现在眼神。");
		return stringBuilder.ToString().Trim();
	}

	private static string BuildSimulatedStoryboardBreakdownDescriptions(WorkflowDocument document, WorkflowNode node)
	{
		string value = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "故事大纲")?.Params?.Setting ?? "现代都市 (Modern City)";
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("分镜图拆解卡");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder2);
		handler.AppendLiteral("主场景世界：");
		handler.AppendFormatted(value);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine("1. 开场镜头：建立人物处境与空间关系，适合给出冲突第一击。");
		stringBuilder.AppendLine("2. 推进镜头：抓角色互动、动作和情绪推进，适合中近景。");
		stringBuilder.AppendLine("3. 悬念镜头：停留在线索、道具或人物反应上，形成钩子。");
		stringBuilder.AppendLine("镜头重点：近景抓表情，中景抓互动，广角交代人物关系位置。");
		stringBuilder.AppendLine("光影建议：主冲突用硬光和冷暖对比，情绪戏用边缘光和局部暗部。");
		return stringBuilder.ToString().Trim();
	}

	private static string BuildSimulatedCharacterViewSummary(WorkflowDocument document, WorkflowNode node)
	{
		string value = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "人物描述")?.Output;
		return string.IsNullOrWhiteSpace(value) ? "角色视图：生成一张主角立绘，突出服装、脸部辨识度和姿态。" : ("角色视图：基于上游角色设定生成统一风格立绘。" + TrimForSummary(value, 180));
	}

	private static string BuildSimulatedStoryboardImageSummary(WorkflowDocument document, WorkflowNode node)
	{
		string value = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "分镜图拆解")?.Output;
		return string.IsNullOrWhiteSpace(value) ? "分镜图片：生成一张镜头感明确的关键画面，包含角色动作、环境、光线和构图。" : ("分镜图片：基于上游分镜图拆解生成关键画面。" + TrimForSummary(value, 180));
	}

	private static string BuildStructuredOutlineSimulation(WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
			WorkflowNodeParameters workflowNodeParameters3 = workflowNodeParameters;
		}
		node.Params.EnsureDefaults(node.Type);
		string text = (string.IsNullOrWhiteSpace(node.Params.CoreIdea) ? "一个普通人被卷入超常秩序与情感试炼的短剧故事" : node.Params.CoreIdea.Trim());
		string text2 = (string.IsNullOrWhiteSpace(node.Params.Genre) ? "都市 (Urban)" : node.Params.Genre);
		string text3 = (string.IsNullOrWhiteSpace(node.Params.Setting) ? "现代都市 (Modern City)" : node.Params.Setting);
		string visualStyleDisplayName = WorkflowNodeParameters.GetVisualStyleDisplayName(node.Params.VisualStyle);
		int num = Math.Max(1, node.Params.Episodes);
		decimal value = ((node.Params.DurationMinutes <= 0m) ? 1m : node.Params.DurationMinutes);
		string value2 = GuessStructuredDramaTitle(text, text2) + "·" + PickOne<string>("夜局", "逆焰", "潮声", "迷城", "焰局");
		string value3 = (text2.Contains("悬疑", StringComparison.Ordinal) ? PickOne<string>("真相与代价", "迷局与人心", "秘密与清算") : (text2.Contains("复仇", StringComparison.Ordinal) ? PickOne<string>("蛰伏与反击", "失衡与清算", "隐忍与翻盘") : (text2.Contains("甜宠", StringComparison.Ordinal) ? PickOne<string>("治愈与双向奔赴", "守护与靠近", "误会与心动") : ((text2.Contains("奇幻", StringComparison.Ordinal) || text3.Contains("仙", StringComparison.Ordinal)) ? PickOne<string>("命运与因果", "誓约与轮回", "宿命与选择") : PickOne<string>("逆袭与自我证明", "身份反差与成长", "生存与情感博弈")))));
		string text4 = PickOne<string>("高密度冲突、强情绪反转与持续悬念", "强反差身份、情感拉扯与连续高能钩子", "高压困局、暧昧博弈与层层升级的反转");
		string text5 = PickOne<string>("沈知微", "顾晚舟", "林昭宁", "许星遥");
		string text6 = PickOne<string>("程砚舟", "裴照临", "陆沉川", "周既明");
		string text7 = PickOne<string>("闻叙白", "纪临渊", "贺沉砚", "秦照野");
		string text8 = PickOne<string>("林妙", "唐岁禾", "乔未晞", "孟知夏");
		string text9 = PickOne<string>("周叔", "韩叔", "罗叔", "秦叔");
		string text10 = PickOne<string>("宋临", "祁野", "顾澄", "陆启安");
		string text11 = PickOne<string>("许未央", "贺霜", "陈惊鹤", "沈烈");
		string text12 = PickOne<string>("旧案编号匣", "封存卷宗箱", "密钥档案盒");
		string text13 = PickOne<string>("匿名终端", "折叠情报机", "幽灵通讯器");
		string text14 = PickOne<string>("倒计时凭证", "限时契约卡", "红线通行签");
		string text15 = PickOne<string>("通行卡", "夜间门禁牌", "临时通行签");
		string text16 = PickOne<string>("录音笔", "口袋录音器", "微型声纹笔");
		string text17 = PickOne<string>("备用手机", "匿名备机", "隐线通讯机");
		string text18 = PickOne<string>("夜班工牌", "值班胸卡", "深夜巡场证");
		string text19 = PickOne<string>("监控截图", "失真监控帧", "异常抓拍图");
		string text20 = PickOne<string>("城市地图", "地下路线图", "旧城区布防图");
		List<string> list = BuildStructuredEpisodePlans(num, text).ToList();
		string[] array = PickOne(new string[5] { "命运开局", "关系失衡", "真相逼近", "正面反击", "终局落点" }, new string[5] { "误入深水", "信任裂缝", "暗线浮出", "局势反咬", "余波定局" }, new string[5] { "风暴前夜", "盟友失真", "秘密穿孔", "反手破局", "落幕回响" });
		int num2 = Math.Min(array.Length, Math.Max(3, (int)Math.Ceiling((double)Math.Min(num, 15) / 3.0)));
		int num3 = num / num2;
		int num4 = num % num2;
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("# 剧名 (Title): 《");
		handler.AppendFormatted(value2);
		handler.AppendLiteral("》");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(50, 3, stringBuilder2);
		handler.AppendLiteral("**一句话梗概 (Logline)**: ");
		handler.AppendFormatted(text);
		handler.AppendLiteral("，在“");
		handler.AppendFormatted(text2);
		handler.AppendLiteral(" / ");
		handler.AppendFormatted(text3);
		handler.AppendLiteral("”框架下展开高密度冲突、强情绪反转与持续悬念。");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("**类型 (Genre)**: ");
		handler.AppendFormatted(text2);
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("**主题 (Theme)**: ");
		handler.AppendFormatted(value3);
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("**背景 (Setting)**: ");
		handler.AppendFormatted(text3);
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(36, 1, stringBuilder2);
		handler.AppendLiteral("**视觉风格**: ");
		handler.AppendFormatted(visualStyleDisplayName);
		handler.AppendLiteral("（建议保持统一角色辨识度、环境层次与高反差情绪光影）");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(25, 2, stringBuilder2);
		handler.AppendLiteral("**总集数**: ");
		handler.AppendFormatted(num);
		handler.AppendLiteral(" | **单集时长**: ");
		handler.AppendFormatted(value, "0.#");
		handler.AppendLiteral(" 分钟");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## 主要人物小传");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 核心角色");
		stringBuilder.AppendLine("* **沈知微**：女主，24岁，表面克制理性，实则倔强敏锐。她因一场意外与主线秘密绑定，被迫从局外人变成破局者。性格：强撑、清醒、护短。能力 / 特征：记忆力极强，擅长从细节中拼出真相。");
		stringBuilder.AppendLine("* **程砚舟**：男主，27岁，外冷内烈，身份看似站在秩序一方，实际上背负无法公开的旧案。性格：寡言、强势、执行力高。能力 / 特征：资源调度能力强，关键时刻敢于越线保护核心人物。");
		stringBuilder.AppendLine("* **闻叙白**：核心对立角色，30岁上下，表面温和克制，实际上最懂如何操纵规则与舆论。性格：冷静、耐心、善于布局。能力 / 特征：擅长制造误会、切断主角联盟。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 重要配角");
		stringBuilder.AppendLine("* **林妙**：女主闺蜜，负责情绪支撑与信息补位，在主角失衡时承担“拉回现实”的作用。");
		stringBuilder.AppendLine("* **周叔**：看似不起眼的中间人，掌握旧线索和关键证物的去向。");
		stringBuilder.AppendLine("* **宋临**：男主身边的助手，忠诚但立场摇摆，是推进误会与反转的重要接口。");
		stringBuilder.AppendLine("* **许未央**：反派阵营中的执行者，手段凌厉，负责制造中期危机。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 其他角色");
		stringBuilder.AppendLine("* **店长老冯**：负责日常场景中的生活感和烟火气。");
		stringBuilder.AppendLine("* **物业经理**：推动背景事件，制造公共冲突场。");
		stringBuilder.AppendLine("* **直播主持人**：负责放大舆论压力。");
		stringBuilder.AppendLine("* **急诊护士**：在关键节点为主角提供短暂窗口。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## 关键物品设定");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 核心物品");
		stringBuilder.AppendLine("* **旧案编号盒**：存放关键资料与历史证据的锁盒，是牵出主线真相的核心钥匙。");
		stringBuilder.AppendLine("* **匿名终端**：来源不明的通讯设备，会在关键节点推送只够看见一半真相的信息。");
		stringBuilder.AppendLine("* **倒计时凭证**：能够指向下一次重大事件发生前的时间窗口，强化剧情紧迫感。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 辅助物品");
		stringBuilder.AppendLine("* **通行卡**：进入限制区域的临时钥匙，通常出现在章节转折点。");
		stringBuilder.AppendLine("* **录音笔**：记录交易或对话，常作为误会澄清与反杀证据。");
		stringBuilder.AppendLine("* **备用手机**：承担身份切换、隐秘联络和信息转移功能。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 世界物品");
		stringBuilder.AppendLine("* **夜班工牌**：强化人物日常身份与现实压力。");
		stringBuilder.AppendLine("* **监控截图**：低成本但高频出现的剧情证据。");
		stringBuilder.AppendLine("* **城市地图**：串联多场景追逐与错位相遇。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
		handler.AppendLiteral("## 章节结构规划（共 ");
		handler.AppendFormatted(num);
		handler.AppendLiteral(" 集）");
		stringBuilder10.AppendLine(ref handler);
		stringBuilder.AppendLine();
		int num5 = 1;
		for (int i = 0; i < num2; i++)
		{
			int num6 = num3 + ((i < num4) ? 1 : 0);
			int num7 = num5;
			int num8 = Math.Min(num, num5 + num6 - 1);
			string value4 = ((i == num2 - 1) ? "终局落点" : ((i == num2 - 2) ? "大转折" : ((i == 0) ? "设定建立" : "小高潮")));
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 4, stringBuilder2);
			handler.AppendLiteral("#### 第");
			handler.AppendFormatted(i + 1);
			handler.AppendLiteral("章：");
			handler.AppendFormatted(array[i]);
			handler.AppendLiteral("（第 ");
			handler.AppendFormatted(num7);
			handler.AppendLiteral("-");
			handler.AppendFormatted(num8);
			handler.AppendLiteral(" 集）");
			stringBuilder11.AppendLine(ref handler);
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("**涉及角色**：沈知微、程砚舟、闻叙白，以及与当前章节冲突最直接相关的配角。");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
			handler.AppendLiteral("**关键物品**：旧案编号盒、匿名终端");
			handler.AppendFormatted((i >= 1) ? "、倒计时凭证" : string.Empty);
			stringBuilder12.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(66, 1, stringBuilder2);
			handler.AppendLiteral("**章节剧情**：围绕“");
			handler.AppendFormatted(TrimForSummary(text, 26));
			handler.AppendLiteral("”展开。主角关系从试探走向绑定，再从合作走向撕裂与重构；外部压力持续升级，促使人物在情感与利益之间做出选择。");
			stringBuilder13.AppendLine(ref handler);
			stringBuilder.AppendLine();
			for (int j = num7; j <= num8 && j - 1 < list.Count; j++)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder14 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
				handler.AppendLiteral("- 第 ");
				handler.AppendFormatted(j);
				handler.AppendLiteral(" 集：");
				handler.AppendFormatted(list[j - 1]);
				stringBuilder14.AppendLine(ref handler);
			}
			stringBuilder.AppendLine();
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder15 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
			handler.AppendLiteral("**关键节点**：");
			handler.AppendFormatted(value4);
			stringBuilder15.AppendLine(ref handler);
			stringBuilder.AppendLine();
			num5 = num8 + 1;
		}
		return stringBuilder.ToString().Trim();
	}

	private static string BuildStructuredScriptSimulation(WorkflowDocument document, WorkflowNode node, string input)
	{
		WorkflowNode workflowNode = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "故事大纲");
		int val = Math.Max(1, workflowNode?.Params?.Episodes ?? 6);
		decimal value = workflowNode?.Params?.DurationMinutes ?? 1m;
		string value2 = GuessStructuredDramaTitle(workflowNode?.Params?.CoreIdea ?? input, workflowNode?.Params?.Genre);
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
		handler.AppendLiteral("# 分集剧本：");
		handler.AppendFormatted(value2);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(54, 1, stringBuilder2);
		handler.AppendLiteral("**节奏说明**：单集 ");
		handler.AppendFormatted(value, "0.#");
		handler.AppendLiteral(" 分钟，采用“开场钩子 -> 冲突升级 -> 情绪反转 -> 结尾留扣”的短剧结构。");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine();
		foreach (int item in Enumerable.Range(1, Math.Min(val, 12)))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
			handler.AppendLiteral("## 第 ");
			handler.AppendFormatted(item);
			handler.AppendLiteral(" 集");
			stringBuilder5.AppendLine(ref handler);
			stringBuilder.AppendLine("### 开场钩子");
			stringBuilder.AppendLine("主角在公开场景中遭遇压迫、误解或失控局面，必须当场做出选择，迅速抛出本集冲突。");
			stringBuilder.AppendLine("### 中段推进");
			stringBuilder.AppendLine("主角与搭档或对立方短暂结盟，围绕核心线索展开调查、交换条件或反向试探，情绪张力持续抬高。");
			stringBuilder.AppendLine("### 结尾留扣");
			stringBuilder.AppendLine("通过新证据、新误会或身份反转，让下一集拥有明确推进方向。");
			stringBuilder.AppendLine();
		}
		return stringBuilder.ToString().Trim();
	}

	private static string BuildStructuredCharacterSimulation(WorkflowDocument document, WorkflowNode node)
	{
		string visualStyleDisplayName = WorkflowNodeParameters.GetVisualStyleDisplayName(WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "故事大纲")?.Params?.VisualStyle ?? "ANIME");
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("## 角色设定卡");
		stringBuilder.AppendLine("### 女主");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(32, 1, stringBuilder2);
		handler.AppendLiteral("- 外形：五官利落，服装线条干净，整体保持 ");
		handler.AppendFormatted(visualStyleDisplayName);
		handler.AppendLiteral(" 风格下的高辨识度。");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine("- 性格：外冷内热、抗压强、嘴硬心软，遇到不公会本能反击。");
		stringBuilder.AppendLine("- 动机：证明自己、保护重要的人，并亲手揭开被隐藏的过去。");
		stringBuilder.AppendLine("- 镜头关键词：近景情绪眼神、快步推进、克制爆发。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 男主");
		stringBuilder.AppendLine("- 外形：轮廓清晰，穿搭克制高级，站姿稳定，压迫感强。");
		stringBuilder.AppendLine("- 性格：寡言、强势、判断快，但在核心关系面前会短暂失控。");
		stringBuilder.AppendLine("- 动机：维持秩序、掩盖旧案，同时在关键时刻守住真正重要的人。");
		stringBuilder.AppendLine("- 镜头关键词：逆光侧脸、慢推镜头、压低视线后的情绪松动。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("### 反派 / 配角");
		stringBuilder.AppendLine("- 反派：永远比主角慢半拍暴露真实意图，擅长制造误会和舆论陷阱。");
		stringBuilder.AppendLine("- 闺蜜 / 助手：承担信息桥梁和现实锚点，让主角关系更有层次。");
		return stringBuilder.ToString().Trim();
	}

	private static string BuildStructuredStoryboardBreakdownSimulation(WorkflowDocument document, WorkflowNode node)
	{
		string value = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "故事大纲")?.Params?.Setting ?? "现代都市 (Modern City)";
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("## 分镜图拆解");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
		handler.AppendLiteral("**主场景世界**：");
		handler.AppendFormatted(value);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine("### 开场镜头");
		stringBuilder.AppendLine("- 先建立人物与空间关系，给出当前冲突和主体站位。");
		stringBuilder.AppendLine("### 推进镜头");
		stringBuilder.AppendLine("- 通过中近景和机位变化强调动作、互动和情绪推进。");
		stringBuilder.AppendLine("### 悬念镜头");
		stringBuilder.AppendLine("- 停留在道具、眼神或角色反应上，为下一个镜头制造钩子。");
		stringBuilder.AppendLine("### 构图建议");
		stringBuilder.AppendLine("- 人物主次明确，前景/中景/背景层次清楚，避免信息堆叠。");
		stringBuilder.AppendLine("### 光影建议");
		stringBuilder.AppendLine("- 高压戏使用冷暖对比与硬光，情绪戏增加边缘光与暗部留白，强化人物状态变化。");
		return stringBuilder.ToString().Trim();
	}

	private static string BuildStructuredCharacterViewSummary(WorkflowDocument document, WorkflowNode node)
	{
		string value = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "人物描述")?.Output;
		return string.IsNullOrWhiteSpace(value) ? "角色视图：生成一张主角立绘，突出服装剪影、脸部辨识度和带情绪的站姿。" : ("角色视图：基于上游人物设定生成统一风格立绘。" + TrimForSummary(value, 180));
	}

	private static string BuildStructuredStoryboardImageSummary(WorkflowDocument document, WorkflowNode node)
	{
		string value = WorkflowExecutor.CollectUpstreamNodes(document, node).FirstOrDefault((WorkflowNode candidate) => candidate.Type == "分镜图拆解")?.Output;
		return string.IsNullOrWhiteSpace(value) ? "分镜图片：生成一张镜头感明确的关键画面，包含环境层次、角色动作、光线方向和构图。" : ("分镜图片：基于上游分镜图拆解生成关键画面。" + TrimForSummary(value, 180));
	}

	private static IEnumerable<string> BuildStructuredEpisodePlans(int episodes, string coreIdea)
	{
		foreach (int episodeIndex in Enumerable.Range(1, Math.Min(episodes, 12)))
		{
			if (1 == 0)
			{
			}
			string text = episodeIndex switch
			{
				1 => "人物登场，主角被卷入第一层冲突，埋下主线种子。", 
				2 => "误会升级，角色关系被迫绑定，合作与试探同时发生。", 
				3 => "利益对撞爆发，情绪升温，出现第一次小高潮。", 
				4 => "短暂联手，表面缓和，暗线信息开始汇流。", 
				5 => "真相露出一角，新对手或新规则进入视野。", 
				6 => "关系反噬，信任破裂，主角必须单独做选择。", 
				7 => "身份或旧事被揭穿，主要角色立场发生偏移。", 
				8 => "情绪跌入低谷，推动主角正视真正欲望。", 
				9 => "关键证据出现，局势发生明显反转。", 
				10 => "正面硬碰硬，情感与立场同时摊牌。", 
				11 => "终局布局完成，反派主动收网或被迫现身。", 
				_ => "主线收束，完成情感落点与命运抉择。", 
			};
			if (1 == 0)
			{
			}
			string text2 = text;
			string beat = text2;
			yield return "围绕“" + TrimForSummary(coreIdea, 24) + "”推进，重点是：" + beat;
		}
	}

	private static string GuessStructuredDramaTitle(string? seed, string? genre)
	{
		string text = (seed ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = genre ?? "命运反转";
		}
		text = new string(text.Where((char ch) => !char.IsWhiteSpace(ch) && !char.IsPunctuation(ch)).ToArray());
		if (text.Length > 8)
		{
			text = text.Substring(0, 8);
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return "逆光而行";
		}
		return (text.Length <= 4) ? (text + "风云") : text;
	}

	private static async Task<string> ExecuteTextNodeAsync(ModelInfo model, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		var requestBody = new
		{
			model = model.Id,
			temperature = GetTextTemperature(node.Type),
			messages = new object[2]
			{
				new
				{
					role = "system",
					content = "你是一个擅长中文漫剧、短剧和分镜工作流的创作助手。输出直接可交给下一个节点继续使用，不要写额外解释。"
				},
				new
				{
					role = "user",
					content = WorkflowExecutor.BuildTextPrompt(node, input)
				}
			}
		};
		Exception lastError = null;
		foreach (string baseUrl in GetApiBaseUrlCandidates(model))
		{
			try
			{
				using HttpRequestMessage request = new HttpRequestMessage(requestUri: new Uri(new Uri(AppendTrailingSlash(baseUrl)), "chat/completions"), method: HttpMethod.Post)
				{
					Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
				};
				ApplyAuthorizationHeader(request, model);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
				string json = await response.Content.ReadAsStringAsync(cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"文本模型调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
				}
				using JsonDocument document = JsonDocument.Parse(json);
				if (TryReadChatCompletionContent(document.RootElement, out var content) && !string.IsNullOrWhiteSpace(content))
				{
					ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(document.RootElement));
					return content.Trim();
				}
				if (TryFindTextCompletionFallbackContent(document.RootElement, out var fallbackContent))
				{
					ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(document.RootElement));
					return fallbackContent.Trim();
				}
				throw new InvalidOperationException("文本模型返回格式无法识别。");
			}
			catch (Exception ex)
			{
				lastError = ex;
			}
		}
		throw new InvalidOperationException(lastError?.Message ?? "文本模型调用失败。", lastError);
	}

	private static async Task<string> ExecuteTextNodeWithFallbackAsync(ModelSettings settings, ModelInfo primaryModel, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		try
		{
			return await ExecuteTextNodeAsync(primaryModel, node, input, cancellationToken);
		}
		catch (Exception primaryError)
		{
			ModelInfo? fallbackModel = ResolveAlternateTextModel(settings, primaryModel);
			if (fallbackModel == null)
			{
				throw CreateFriendlyTextModelException(primaryModel, primaryError);
			}

			try
			{
				return await ExecuteTextNodeAsync(fallbackModel, node, input, cancellationToken);
			}
			catch (Exception fallbackError)
			{
				throw new InvalidOperationException(
					$"文本模型调用失败：本地/首选模型“{primaryModel.Name.OrDefault(primaryModel.Id)}”失败：{ExtractFriendlyExceptionMessage(primaryError)}；备用模型“{fallbackModel.Name.OrDefault(fallbackModel.Id)}”也失败：{ExtractFriendlyExceptionMessage(fallbackError)}",
					fallbackError);
			}
		}
	}

	private static void ApplyTextNodeResult(WorkflowNode node, string input, string result)
	{
		if (node.Type == "故事剧本")
		{
			node.Params ??= new WorkflowNodeParameters();
			node.Params.EnsureDefaults(node.Type);
			node.Params.GeneratedScriptEpisodes = WorkflowExecutor.NormalizeGeneratedScriptEpisodeCount(
				node,
				input,
				WorkflowExecutor.ParseGeneratedScriptEpisodes(result));
			if (node.Params.GeneratedScriptEpisodes.Count > 0)
			{
				node.Params.SelectedScriptEpisodeIndex = Math.Min(Math.Max(0, node.Params.SelectedScriptEpisodeIndex), node.Params.GeneratedScriptEpisodes.Count - 1);
				node.Output = WorkflowExecutor.BuildScriptEpisodesOutput(node.Params.GeneratedScriptEpisodes);
				node.ArtifactPath = string.Empty;
				node.ArtifactKind = string.Empty;
			}
			else
			{
				node.Params.GeneratedScriptEpisodes.Clear();
				node.Params.SelectedScriptEpisodeIndex = 0;
				WorkflowExecutor.ApplyResult(node, input, result);
			}
		}
		else if (node.Type == "分镜图拆解")
		{
			node.Params ??= new WorkflowNodeParameters();
			node.Params.EnsureDefaults(node.Type);
			node.Params.StoryboardShots = WorkflowExecutor.ParseStoryboardShots(result);
			if (node.Params.StoryboardShots.Count > 0)
			{
				node.Output = WorkflowExecutor.BuildStoryboardBreakdownOutput(node);
				node.ArtifactPath = string.Empty;
				node.ArtifactKind = string.Empty;
			}
			else
			{
				WorkflowExecutor.ApplyResult(node, input, result);
			}
		}
		else
		{
			WorkflowExecutor.ApplyResult(node, input, result);
		}
	}

	private static Exception CreateFriendlyTextModelException(ModelInfo model, Exception error)
	{
		string modelName = model.Name.OrDefault(model.Id).OrDefault("未命名文本模型");
		string message = ExtractFriendlyExceptionMessage(error);
		return new InvalidOperationException($"文本模型“{modelName}”调用失败：{message}。请确认本地模型服务正在运行，或在模型设置里配置一个可用的云端文本模型。", error);
	}

	private static string ExtractFriendlyExceptionMessage(Exception error)
	{
		for (Exception? current = error; current != null; current = current.InnerException)
		{
			if (current is SocketException socketException)
			{
				return socketException.SocketErrorCode == SocketError.OperationAborted
					? "连接被本地模型服务提前中断"
					: socketException.Message;
			}
		}

		return string.IsNullOrWhiteSpace(error.Message) ? "未知错误" : error.Message;
	}

	private static double GetTextTemperature(string nodeType)
	{
		bool flag = false;
		double result = ((nodeType == "故事大纲") ? 0.45 : ((!(nodeType == "故事剧本")) ? 0.7 : 0.6));
		bool flag2 = false;
		return result;
	}

	private static async Task<string> ExecuteTextCompletionAsync(ModelInfo model, string prompt, CancellationToken cancellationToken, double temperature, string? systemPrompt = null, string? moduleName = null)
	{
		EnsureLocalOnlyModel(model, "文本模型");
		PublishPrompt(moduleName ?? "文本补全", model, prompt);
		var requestBody = new
		{
			model = model.Id,
			temperature = temperature,
			messages = new object[2]
			{
				new
				{
					role = "system",
					content = (systemPrompt ?? "你是一位擅长中文短剧、漫剧、分镜与提示词设计的创作助手。只输出最终可直接使用的结果，不要解释过程。")
				},
				new
				{
					role = "user",
					content = prompt
				}
			}
		};
		Exception lastError = null;
		foreach (string baseUrl in GetApiBaseUrlCandidates(model))
		{
			try
			{
				using HttpRequestMessage request = new HttpRequestMessage(requestUri: new Uri(new Uri(AppendTrailingSlash(baseUrl)), "chat/completions"), method: HttpMethod.Post)
				{
					Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
				};
				ApplyAuthorizationHeader(request, model);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
				string json = await response.Content.ReadAsStringAsync(cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"文本模型调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
				}
				using JsonDocument document = JsonDocument.Parse(json);
				if (TryReadChatCompletionContent(document.RootElement, out var content) && !string.IsNullOrWhiteSpace(content))
				{
					ModelCallLogService.LogSuccess(moduleName ?? "文本补全", model, ModelCallUsage.FromJson(document.RootElement));
					return content.Trim();
				}
				if (TryFindTextCompletionFallbackContent(document.RootElement, out var fallbackContent))
				{
					ModelCallLogService.LogSuccess(moduleName ?? "文本补全", model, ModelCallUsage.FromJson(document.RootElement));
					return fallbackContent.Trim();
				}
				throw new InvalidOperationException("文本模型返回格式无法识别。");
			}
			catch (Exception ex)
			{
				lastError = ex;
			}
		}
		throw new InvalidOperationException(lastError?.Message ?? "文本模型调用失败。", lastError);
	}

	private static async Task<bool> TryExecuteMediaTextFallbackAsync(ModelSettings settings, WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken, string reason)
	{
		try
		{
			switch (node.Type)
			{
			case "角色设计":
			case "分镜图片":
			{
				ModelInfo textModel2 = ResolveTextFallbackModel(settings);
				if (textModel2 == null || string.IsNullOrWhiteSpace(textModel2.Url))
				{
					return false;
				}
				string prompt = await ExecuteTextCompletionAsync(textModel2, WorkflowExecutor.BuildImagePrompt(node, input), cancellationToken, 0.55, null, node.Type + "/提示词兜底");
				string promptOutput = $"# {node.Type}提示词{Environment.NewLine}{Environment.NewLine}{WorkflowExecutor.NormalizeTextResult(node.Type, prompt)}{Environment.NewLine}{Environment.NewLine}> 生成方式：{reason}";
				string filePath3 = SaveSimulatedTextArtifact(node, "txt", promptOutput, "prompt");
				WorkflowExecutor.ApplyStructuredArtifactResult(node, promptOutput, filePath3, "prompt");
				return true;
			}
			case "分镜视频":
			{
				ModelInfo textModel = ResolveTextFallbackModel(settings);
				if (textModel == null || string.IsNullOrWhiteSpace(textModel.Url))
				{
					return false;
				}
				string normalizedStoryboard = WorkflowExecutor.NormalizeTextResult(result: await ExecuteTextCompletionAsync(textModel, WorkflowExecutor.BuildStoryboardFallbackPrompt(node, input), cancellationToken, 0.45, null, node.Type + "/文本兜底"), nodeType: node.Type);
				string filePath2 = SaveSimulatedTextArtifact(node, "json", normalizedStoryboard, "storyboard");
				WorkflowExecutor.ApplyStructuredArtifactResult(node, normalizedStoryboard, filePath2, "storyboard");
				return true;
			}
			case "视频合集":
			{
				string plan = BuildVideoCollectionPlan(document, node, input, reason);
				string filePath = SaveSimulatedTextArtifact(node, "md", plan, "collection_plan");
				WorkflowExecutor.ApplyStructuredArtifactResult(node, plan, filePath, "collection");
				return true;
			}
			}
		}
		catch
		{
			return false;
		}
		return false;
	}

	private static ModelInfo? ResolveTextFallbackModel(ModelSettings settings)
	{
		ModelInfo modelInfo = ModelConfig.GetPreferredLocalTextModel(settings);
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		modelInfo = settings.Models.FirstOrDefault((ModelInfo model) => model.Category == ModelCategory.Text && ModelConfig.MatchesModelSelector(model, settings.SelectedTextModel));
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		modelInfo = ModelConfig.GetPreferredCloudTextModel(settings);
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		return settings.Models.FirstOrDefault((ModelInfo model) => model.Category == ModelCategory.Text && !string.IsNullOrWhiteSpace(model.Url));
	}

	private static ModelInfo? ResolveAlternateTextModel(ModelSettings settings, ModelInfo failedModel)
	{
		IEnumerable<ModelInfo?> candidates = new ModelInfo?[]
		{
			ModelConfig.GetPreferredLocalTextModel(settings),
			settings.Models.FirstOrDefault(model =>
				model.Category == ModelCategory.Text &&
				ModelConfig.MatchesModelSelector(model, settings.SelectedTextModel) &&
				!string.IsNullOrWhiteSpace(model.Url)),
			ModelConfig.GetPreferredCloudTextModel(settings),
		}
		.Concat(settings.Models
			.Where(model => model.Category == ModelCategory.Text && !string.IsNullOrWhiteSpace(model.Url))
			.OrderBy(model => ModelConfig.IsLocalEndpointUrl(model.Url) ? 0 : 1));

		return candidates
			.Where(model => model != null && !IsSameModel(model, failedModel))
			.DistinctBy(model => $"{ModelConfig.GetModelSelector(model)}|{model!.Url}", StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	private static bool IsSameModel(ModelInfo? left, ModelInfo? right)
	{
		if (left == null || right == null)
		{
			return false;
		}

		return string.Equals(ModelConfig.GetModelSelector(left), ModelConfig.GetModelSelector(right), StringComparison.OrdinalIgnoreCase) &&
			string.Equals(NormalizeApiBaseUrl(left.Url), NormalizeApiBaseUrl(right.Url), StringComparison.OrdinalIgnoreCase);
	}

	private static ModelInfo? ResolveStoryboardVideoPromptTextModel(ModelSettings settings, WorkflowNode node)
	{
		ModelInfo modelInfo = ResolveSelectedModel(settings, node, ModelCategory.Text);
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		modelInfo = ModelConfig.GetPreferredLocalTextModel(settings);
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		modelInfo = ResolveTextFallbackModel(settings);
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		modelInfo = ModelConfig.GetPreferredCloudTextModel(settings);
		if (modelInfo != null && !string.IsNullOrWhiteSpace(modelInfo.Url))
		{
			return modelInfo;
		}
		return null;
	}

	private static ModelInfo? ResolveImagePromptTextModel(ModelSettings settings)
	{
		string preferredId = ModelConfig.GetImagePromptTextModelId(settings);
		ModelInfo modelInfo = settings.Models.FirstOrDefault((ModelInfo model) => model.Category == ModelCategory.Text && ModelConfig.MatchesModelSelector(model, preferredId) && !string.IsNullOrWhiteSpace(model.Url));
		if (modelInfo != null)
		{
			return modelInfo;
		}
		return ResolveTextFallbackModel(settings);
	}

	private static ModelInfo? ResolveNodeImagePromptTextModel(ModelSettings settings, WorkflowNode node)
	{
		ModelInfo modelInfo = ResolveSelectedModel(settings, node, ModelCategory.Text);
		return modelInfo ?? ResolveImagePromptTextModel(settings);
	}

	private static async Task EnsureCharacterProfileAsync(ModelInfo textModel, WorkflowNode node, CharacterDesignEntry entry, string upstreamInput, CancellationToken cancellationToken, bool forceRefresh = false)
	{
		if (!forceRefresh && !string.IsNullOrWhiteSpace(entry.AppearancePrompt) && !string.IsNullOrWhiteSpace(entry.BasicStats) && !string.IsNullOrWhiteSpace(entry.Personality))
		{
			entry.ProfileStatus = CharacterAssetStatus.Success;
			return;
		}
		string normalized = WorkflowExecutor.NormalizeTextResult(result: await ExecuteTextCompletionAsync(textModel, WorkflowExecutor.BuildCharacterProfilePrompt(node, upstreamInput, entry), cancellationToken, 0.45, null, "角色设计/角色档案"), nodeType: node.Type);
		if (!TryApplyCharacterProfileJson(normalized, entry) &&
			!TryApplyLooseCharacterProfileText(normalized, entry))
		{
			ApplyCharacterProfileFallback(node, entry, upstreamInput, normalized);
		}
		if (forceRefresh)
		{
			entry.ExpressionPrompt = string.Empty;
			entry.ThreeViewPrompt = string.Empty;
		}
		entry.ProfileStatus = CharacterAssetStatus.Success;
		entry.Summary = BuildCharacterProfileSummary(entry).OrDefault(CleanCharacterProfileSummary(entry.Summary));
	}

	private static bool TryApplyCharacterProfileJson(string normalized, CharacterDesignEntry entry)
	{
		if (!WorkflowExecutor.TryExtractJsonPayload(normalized, out string jsonPayload))
		{
			return false;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(jsonPayload);
			if (!TryResolveCharacterProfileElement(document.RootElement, out JsonElement profileElement))
			{
				return false;
			}

			ApplyCharacterProfileElement(profileElement, entry);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool TryResolveCharacterProfileElement(JsonElement rootElement, out JsonElement profileElement)
	{
		profileElement = default;
		if (rootElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement element in rootElement.EnumerateArray())
			{
				if (element.ValueKind == JsonValueKind.Object)
				{
					profileElement = element;
					return true;
				}
			}

			return false;
		}

		if (rootElement.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		if (LooksLikeCharacterProfileObject(rootElement))
		{
			profileElement = rootElement;
			return true;
		}

		foreach (string propertyName in new[] { "character", "profile", "characterProfile", "character_profile", "data", "result" })
		{
			if (TryGetJsonProperty(rootElement, propertyName, out JsonElement nestedElement))
			{
				if (nestedElement.ValueKind == JsonValueKind.Object && LooksLikeCharacterProfileObject(nestedElement))
				{
					profileElement = nestedElement;
					return true;
				}

				if (nestedElement.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in nestedElement.EnumerateArray())
					{
						if (item.ValueKind == JsonValueKind.Object && LooksLikeCharacterProfileObject(item))
						{
							profileElement = item;
							return true;
						}
					}
				}
			}
		}

		profileElement = rootElement;
		return true;
	}

	private static bool LooksLikeCharacterProfileObject(JsonElement element)
	{
		return TryGetJsonProperty(element, "name", out _) ||
			TryGetJsonProperty(element, "basicStats", out _) ||
			TryGetJsonProperty(element, "appearancePrompt", out _) ||
			TryGetJsonProperty(element, "personality", out _);
	}

	private static void ApplyCharacterProfileElement(JsonElement profileElement, CharacterDesignEntry entry)
	{
		ApplyReturnedCharacterName(entry, ReadJsonProperty(profileElement, "name"));
		entry.Alias = ReadJsonProperty(profileElement, "alias").OrDefault(entry.Alias);
		string roleText = ReadJsonProperty(profileElement, "role");
		if (!string.IsNullOrWhiteSpace(roleText))
		{
			entry.RoleType = ((roleText.Contains("配", StringComparison.Ordinal) || roleText.Contains("反派", StringComparison.Ordinal)) ? CharacterDesignRoleType.Supporting.ToLabel() : CharacterDesignRoleType.Main.ToLabel());
		}
		entry.BasicStats = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(ReadJsonProperty(profileElement, "basicStats").OrDefault(entry.BasicStats));
		entry.Profession = ReadJsonProperty(profileElement, "profession").OrDefault(entry.Profession);
		entry.Background = ReadJsonProperty(profileElement, "background").OrDefault(entry.Background);
		entry.Personality = ReadJsonProperty(profileElement, "personality").OrDefault(entry.Personality);
		entry.Motivation = ReadJsonProperty(profileElement, "motivation").OrDefault(entry.Motivation);
		entry.Values = ReadJsonProperty(profileElement, "values").OrDefault(entry.Values);
		entry.Weakness = ReadJsonProperty(profileElement, "weakness").OrDefault(entry.Weakness);
		entry.Relationships = ReadJsonProperty(profileElement, "relationships").OrDefault(entry.Relationships);
		entry.Habits = ReadJsonProperty(profileElement, "habits").OrDefault(entry.Habits);
		entry.VisualTags = ReadJsonProperty(profileElement, "visualTags").OrDefault(entry.VisualTags);
		entry.AppearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(ReadJsonProperty(profileElement, "appearancePrompt").OrDefault(entry.AppearancePrompt));
		entry.CostumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(ReadJsonProperty(profileElement, "costumeNotes").OrDefault(entry.CostumeNotes));
		entry.ActingNotes = ReadJsonProperty(profileElement, "actingNotes").OrDefault(entry.ActingNotes);
	}

	private static void ApplyReturnedCharacterName(CharacterDesignEntry entry, string returnedName)
	{
		returnedName = WorkflowParseHelpers.CleanExtractedValue(returnedName);
		if (string.IsNullOrWhiteSpace(returnedName) ||
			string.Equals(returnedName, entry.Name, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(entry.Name))
		{
			entry.Name = returnedName;
			return;
		}

		if (string.IsNullOrWhiteSpace(entry.Alias))
		{
			entry.Alias = returnedName;
			return;
		}

		var aliases = entry.Alias
			.Split(new[] { '/', '／', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (aliases.Any(alias => string.Equals(alias, returnedName, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		entry.Alias = $"{entry.Alias} / {returnedName}";
	}

	private static bool TryApplyLooseCharacterProfileText(string text, CharacterDesignEntry entry)
	{
		if (string.IsNullOrWhiteSpace(text) ||
			(!text.Contains("\"name\"", StringComparison.OrdinalIgnoreCase) &&
			 !text.Contains("'name'", StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		bool applied = false;
		applied |= ApplyLooseProfileField(text, string.Empty, value => ApplyReturnedCharacterName(entry, value), "name");
		applied |= ApplyLooseProfileField(text, entry.Alias, value => entry.Alias = value, "alias");
		applied |= ApplyLooseProfileField(text, entry.RoleType, value => entry.RoleType = value, "role");
		applied |= ApplyLooseProfileField(text, entry.BasicStats, value => entry.BasicStats = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(value), "basicStats", "basic_stats");
		applied |= ApplyLooseProfileField(text, entry.Profession, value => entry.Profession = value, "profession");
		applied |= ApplyLooseProfileField(text, entry.Background, value => entry.Background = value, "background");
		applied |= ApplyLooseProfileField(text, entry.Personality, value => entry.Personality = value, "personality");
		applied |= ApplyLooseProfileField(text, entry.Motivation, value => entry.Motivation = value, "motivation");
		applied |= ApplyLooseProfileField(text, entry.Values, value => entry.Values = value, "values");
		applied |= ApplyLooseProfileField(text, entry.Weakness, value => entry.Weakness = value, "weakness");
		applied |= ApplyLooseProfileField(text, entry.Relationships, value => entry.Relationships = value, "relationships");
		applied |= ApplyLooseProfileField(text, entry.Habits, value => entry.Habits = value, "habits");
		applied |= ApplyLooseProfileField(text, entry.VisualTags, value => entry.VisualTags = value, "visualTags", "visual_tags");
		applied |= ApplyLooseProfileField(text, entry.AppearancePrompt, value => entry.AppearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(value), "appearancePrompt", "appearance_prompt");
		applied |= ApplyLooseProfileField(text, entry.CostumeNotes, value => entry.CostumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(value), "costumeNotes", "costume_notes");
		applied |= ApplyLooseProfileField(text, entry.ActingNotes, value => entry.ActingNotes = value, "actingNotes", "acting_notes");
		return applied && (!string.IsNullOrWhiteSpace(entry.BasicStats) || !string.IsNullOrWhiteSpace(entry.Personality) || !string.IsNullOrWhiteSpace(entry.AppearancePrompt));
	}

	private static bool ApplyLooseProfileField(string text, string currentValue, Action<string> apply, params string[] propertyNames)
	{
		if (!string.IsNullOrWhiteSpace(currentValue) && !CharacterDesignEntry.LooksLikeRawStructuredText(currentValue))
		{
			return false;
		}

		string value = ReadLooseJsonStringProperty(text, propertyNames);
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		apply(value);
		return true;
	}

	private static string ReadLooseJsonStringProperty(string text, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			string pattern = $@"(?is)['""]{Regex.Escape(propertyName)}['""]\s*:\s*(?:""(?<dq>(?:\\.|[^""\\])*)""|'(?<sq>(?:\\.|[^'\\])*)')";
			Match match = Regex.Match(text, pattern);
			if (!match.Success)
			{
				continue;
			}

			string raw = match.Groups["dq"].Success ? match.Groups["dq"].Value : match.Groups["sq"].Value;
			if (match.Groups["dq"].Success)
			{
				try
				{
					return JsonSerializer.Deserialize<string>($"\"{raw}\"")?.Trim() ?? string.Empty;
				}
				catch (JsonException)
				{
					return Regex.Unescape(raw).Trim();
				}
			}

			return raw.Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal).Trim();
		}

		return string.Empty;
	}

	private static void ApplyCharacterProfileFallback(WorkflowNode node, CharacterDesignEntry entry, string upstreamInput, string rawProfileText)
	{
		string fallbackSummary = TrimForSummary(FirstUsableProfileSummary(rawProfileText, entry.Summary, upstreamInput, entry.Name), 260);
		if (!string.IsNullOrWhiteSpace(fallbackSummary))
		{
			entry.Summary = fallbackSummary;
		}

		entry.BasicStats = entry.BasicStats.OrDefault(entry.Summary.OrDefault(entry.Name));
		entry.Profession = entry.Profession.OrDefault(entry.RoleType.OrDefault("角色"));
		entry.Personality = entry.Personality.OrDefault("根据剧情设定保持一致");
		entry.VisualTags = entry.VisualTags.OrDefault(WorkflowExecutor.ResolveCharacterDesignStyleDescriptorChinese(node));
		entry.AppearancePrompt = entry.AppearancePrompt.OrDefault(MergeCommaPromptFragments(
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			entry.Name,
			entry.BasicStats,
			entry.CostumeNotes,
			entry.VisualTags,
			"single consistent character design reference"));
		entry.CostumeNotes = entry.CostumeNotes.OrDefault("服装和配饰以故事大纲中的角色设定为准，后续生成时保持一致。");
		entry.ActingNotes = entry.ActingNotes.OrDefault("neutral standing pose, clear face, readable expression, consistent identity");
		entry.BasicStats = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.BasicStats);
		entry.AppearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
		entry.CostumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);
	}

	private static string BuildCharacterProfileSummary(CharacterDesignEntry entry)
	{
		return string.Join(" / ", new[] { entry.BasicStats, entry.Profession, entry.Personality }
			.Select(CleanCharacterProfileSummary)
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Take(3));
	}

	private static string FirstUsableProfileSummary(params string[] values)
	{
		foreach (string value in values)
		{
			string cleaned = CleanCharacterProfileSummary(value);
			if (!string.IsNullOrWhiteSpace(cleaned))
			{
				return cleaned;
			}
		}

		return string.Empty;
	}

	private static string CleanCharacterProfileSummary(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || CharacterDesignEntry.LooksLikeRawStructuredText(value))
		{
			return string.Empty;
		}

		return value.Trim();
	}

	private static (string Positive, string Negative) SplitStoredCharacterPrompt(string? storedPrompt)
	{
		string text = (storedPrompt ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return (string.Empty, string.Empty);
		}

		string labelPattern = "(?:Positive|正向提示词|正向)\\s*[:：]";
		string negativePattern = "(?:Negative|反向提示词|反向)\\s*[:：]";
		Match positiveMatch = Regex.Match(text, labelPattern + "\\s*(?<positive>.*?)(?:\\n\\s*" + negativePattern + "|\\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		Match negativeMatch = Regex.Match(text, negativePattern + "\\s*(?<negative>.*)\\z", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		if (positiveMatch.Success)
		{
			return (
				positiveMatch.Groups["positive"].Value.Trim(),
				negativeMatch.Success ? negativeMatch.Groups["negative"].Value.Trim() : string.Empty);
		}

		if (negativeMatch.Success)
		{
			return (
				text.Substring(0, negativeMatch.Index).Trim(),
				negativeMatch.Groups["negative"].Value.Trim());
		}

		return (text, string.Empty);
	}

	private static (string Positive, string Negative) SanitizeStoredCharacterPrompt((string Positive, string Negative) prompt)
	{
		string positive = IsInvalidCharacterPrompt(prompt.Positive) ? string.Empty : prompt.Positive.Trim();
		string negative = IsInvalidCharacterPrompt(prompt.Negative) ? string.Empty : prompt.Negative.Trim();
		return (positive, negative);
	}

	private static bool IsInvalidCharacterPrompt(string? prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt))
		{
			return true;
		}

		string text = prompt.Trim();
		if (Regex.IsMatch(text, @"^chatcmpl-[A-Za-z0-9_-]+$", RegexOptions.IgnoreCase) ||
			Regex.IsMatch(text, @"^cmpl-[A-Za-z0-9_-]+$", RegexOptions.IgnoreCase))
		{
			return true;
		}

		if ((text.StartsWith("{", StringComparison.Ordinal) || text.StartsWith("[", StringComparison.Ordinal)) &&
			WorkflowExecutor.TryExtractJsonPayload(text, out _))
		{
			return true;
		}

		return text.Length < 24;
	}

	private static string NormalizeCharacterImagePrompt(WorkflowNode node, CharacterDesignEntry entry, string prompt, CharacterDesignActionType action)
	{
		if (IsInvalidCharacterPrompt(prompt))
		{
			return action == CharacterDesignActionType.GenerateExpression
				? BuildCharacterExpressionIdentityFallbackPrompt(node, entry)
				: BuildCharacterThreeViewIdentityFallbackPrompt(node, entry);
		}

		string genderLock = BuildEnglishCharacterGenderLock(entry, fullBody: action == CharacterDesignActionType.GenerateThreeView);
		string identityLock = BuildEnglishCharacterIdentityLock(entry);
		string style = WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node);
		string taskLock = action == CharacterDesignActionType.GenerateExpression
			? "single close-up head-and-shoulders portrait identity prompt, face and hairstyle only, shoulders-above framing only, crop at collarbone, one face portrait only, same face shape and same hairstyle in every expression, same beard and facial hair in every expression if present, clean seamless studio background only, no environment or background objects, no chest, no torso, no arms, no hands"
			: "three-view turnaround identity prompt, front view side view back view only, complete full body visible from head to toe in every view, feet and shoes fully visible, same outfit and same hairstyle in all views, clean seamless studio background only, no environment or background objects, no bags or bag straps, no external objects";
		return MergeCommaPromptFragments(
			prompt,
			genderLock,
			identityLock,
			style,
			taskLock,
			action == CharacterDesignActionType.GenerateExpression ? CharacterExpressionCleanBackgroundPrompt : null,
			action == CharacterDesignActionType.GenerateThreeView ? CharacterThreeViewCleanBackgroundPrompt : null,
			action == CharacterDesignActionType.GenerateThreeView ? CharacterThreeViewNoBagPrompt : null,
			action == CharacterDesignActionType.GenerateThreeView ? CharacterThreeViewNoExternalObjectPrompt : null);
	}

	private static string BuildCharacterExpressionIdentityFallbackPrompt(WorkflowNode node, CharacterDesignEntry entry)
	{
		return MergeCommaPromptFragments(
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			BuildEnglishCharacterGenderLock(entry, fullBody: false),
			BuildEnglishCharacterIdentityLock(entry),
			"single consistent character design reference",
			"exact straight-on front close-up head portrait",
			"face parallel to camera",
			"head level",
			"no head turn",
			"no head tilt",
			"face, neck, collar neckline and top of shoulders only",
			"crop at collarbone",
			"shoulders-above framing",
			CharacterExpressionBustFramingPrompt,
			CharacterExpressionCleanBackgroundPrompt,
			"same exact face shape, same exact hairstyle, same hairline, same bangs, same hair volume, same age, same gender",
			BuildEnglishExpressionFacialHairLock(entry),
			"no chest, no torso, no arms, no hands, no props, no text");
	}

	private static string BuildSingleExpressionCellBasePrompt(WorkflowNode node, CharacterDesignEntry entry, string basePrompt)
	{
		string filteredPrompt = RemovePromptFragments(basePrompt,
			"3x3",
			"3×3",
			"nine-panel",
			"nine panel",
			"nine-grid",
			"nine grid",
			"expression sheet",
			"single composite image",
			"multi-panel",
			"multiple panels",
			"panel layout",
			"grid",
			"collage",
			"contact sheet",
			"character sheet",
			"表情板",
			"九宫格");

		return MergeCommaPromptFragments(
			filteredPrompt,
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			BuildEnglishCharacterGenderLock(entry, fullBody: false),
			BuildEnglishCharacterIdentityLock(entry),
			"single close-up head portrait identity reference",
			"one face portrait only",
			CharacterSingleExpressionPortraitPrompt,
			CharacterExpressionBustFramingPrompt,
			"exact straight-on front view only",
			"face parallel to camera",
			"head level",
			"no head turn",
			"no head tilt",
			"both eyes equally visible",
			"both cheeks symmetrically visible",
			"face, neck, collar neckline and top of shoulders only",
			"crop at collarbone",
			"shoulders-above framing",
			"no chest, no bust, no torso, no arms, no hands",
			"centered composition",
			CharacterExpressionCleanBackgroundPrompt,
			"single portrait composition",
			"one centered face only",
			"one subject only");
	}

	private static string BuildLocalExpressionTargetPrompt(string englishLabel)
	{
		return (englishLabel ?? string.Empty).Trim().ToLowerInvariant() switch
		{
			"neutral expression" => "TARGET EXPRESSION: neutral calm expression, closed lips, relaxed neutral brows, steady eyes open, no smile, no grin, no laugh",
			"smile" => "TARGET EXPRESSION: gentle smile, mouth corners clearly lifted, softened eyes, warm restrained smile, not laughing",
			"angry" => "TARGET EXPRESSION: angry serious frown, eyebrows pulled down and inward, narrowed eyes, tense pressed mouth, no smile, no grin",
			"surprised" => "TARGET EXPRESSION: surprised expression, eyes wide open, eyebrows raised high, mouth slightly open in a small O shape, no smile",
			"sad" => "TARGET EXPRESSION: sad forlorn expression, inner eyebrows raised, softened tired eyes, mouth corners clearly downturned, no smile",
			"laughing" => "TARGET EXPRESSION: laughing happy expression, broad open-mouth laugh, raised cheeks, joyful eyes, visibly different from gentle smile",
			"thinking expression" => "TARGET EXPRESSION: thinking puzzled expression, brows slightly knitted, focused forward gaze, lips pressed, no smile",
			"peaceful" => "TARGET EXPRESSION: peaceful relaxed expression, both eyes gently closed, tiny relaxed closed-mouth smile, calm face",
			"skeptical expression" => "TARGET EXPRESSION: skeptical dismissive expression, one eyebrow raised, narrowed eyes, asymmetric tense mouth, disdainful look, no warm smile",
			_ => englishLabel ?? string.Empty
		};
	}

	private static string BuildLocalExpressionCellPrompt(WorkflowNode node, CharacterDesignEntry entry, string targetExpression)
	{
		return MergeCommaPromptFragments(
			targetExpression,
			CharacterSingleExpressionPortraitPrompt,
			CharacterExpressionBustFramingPrompt,
			"the target expression is mandatory and must be obvious at first glance",
			"do not keep the uploaded reference expression unless the target is neutral",
			"the uploaded front portrait is only an identity, face-shape, hairstyle, and lighting reference",
			"same character as the uploaded front reference portrait",
			"change the facial expression clearly while preserving identity",
			"clearly change eyebrow angle, eyelid openness, gaze focus, cheek tension, mouth corners, lips and jaw tension for the target emotion",
			"exact straight-on front close-up head portrait only",
			"face parallel to camera, head level, no side face, no three-quarter face",
			"keep the full output as one single portrait, never draw multiple expressions in one image",
			"same face shape, same jawline, same nose shape, same base eye shape, same base lip shape",
			"preserve identity while allowing eyebrows, eyelids and mouth expression to change",
			"same hairstyle, same hairline, same bangs, same hair volume, same hair color",
			"same exact outfit anchor, same visible collar, same neckline, same shoulder clothing, no clothing alternatives, no outfit switching",
			BuildEnglishExpressionFacialHairLock(entry),
			"same lighting, same crop, same finished full-color portrait",
			CharacterExpressionCleanBackgroundPrompt,
			BuildEnglishCharacterGenderLock(entry, fullBody: false),
			BuildEnglishCharacterIdentityLock(entry),
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			"centered close-up head-and-shoulders portrait, shoulder-up crop at collarbone, show no chest, no bust, no torso, no waist, no belt, no pants, no hands, no arms, no props, exactly one face only, exactly one head only");
	}

	private static string BuildLocalThreeViewFrontReferencePrompt(WorkflowNode node, CharacterDesignEntry entry)
	{
		return MergeCommaPromptFragments(
			"same character as the uploaded neutral front face reference",
			"generate the first clean front full-body reference of the exact same person",
			"this front full-body image becomes the only costume reference for the side and back views",
			"STRICT FRONT FULL-BODY VIEW ONLY",
			"straight-on orthographic front view, chest facing camera, hips facing camera, both eyes visible, both shoulders even",
			"camera far enough away to show the entire body",
			"the head must stay small relative to the full body and must not dominate the frame",
			CharacterThreeViewFullBodyPrompt,
			"same face, same eyes, same nose, same mouth, same eyebrows",
			"same hairstyle, same beard and facial hair if present",
			"outfit must follow the character setting: same outerwear, same innerwear, same belt, same shoes, same wearable clothing accessories except bags, same colors",
			"only the character may appear, no microphone, no mic stand, no camera, no tripod, no phone, no tools, no weapon, no umbrella, no staff, no suitcase, no box, no chair, no desk, no podium, no cable, no profession equipment",
			"neutral standing pose, arms relaxed at sides, empty hands",
			CharacterThreeViewCleanBackgroundPrompt,
			CharacterThreeViewNoBagPrompt,
			CharacterThreeViewNoExternalObjectPrompt,
			BuildEnglishCharacterGenderLock(entry, fullBody: true),
			BuildEnglishCharacterIdentityLock(entry),
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			"exactly one character only");
	}

	private static string BuildLocalThreeViewCellPrompt(WorkflowNode node, CharacterDesignEntry entry, string viewPrompt)
	{
		return MergeCommaPromptFragments(
			viewPrompt,
			"the requested camera view above is mandatory and overrides the uploaded reference pose",
			"use the uploaded full-body front reference as costume, shoes, accessories, colors, body-proportion and identity anchor only",
			"do not copy the front-facing pose when the requested view is side or back",
			"generate this requested view from the front full-body reference without redesigning the character",
			"strict costume lock from the uploaded front full-body reference",
			"camera far enough away to show the entire body",
			"the head must not dominate the frame, never output a portrait or headshot",
			"same face, same hairstyle, same beard and facial hair if present",
			"copy the exact same outfit from the uploaded reference: same outfit layers, same collar shape, same sleeve length, same belt, same shoes, same wearable clothing accessories except bags, same fabric colors, same trim colors",
			"the outfit, shoes, belt, ornaments, sleeve design, collar design and color palette must match the uploaded front full-body reference in every view, but remove all bags and bag straps",
			"do not change clothes, do not change shoes, do not change clothing color, do not redesign the costume, do not add any bag",
			"do not add any external object, no microphone, no mic stand, no camera, no tripod, no phone, no tools, no weapon, no umbrella, no staff, no suitcase, no box, no chair, no desk, no podium, no cable, no profession equipment",
			"neutral standing pose, arms relaxed at sides, empty hands",
			CharacterThreeViewFullBodyPrompt,
			CharacterThreeViewSingleViewPrompt,
			BuildEnglishCharacterGenderLock(entry, fullBody: true),
			BuildEnglishCharacterIdentityLock(entry),
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			CharacterThreeViewCleanBackgroundPrompt,
			CharacterThreeViewNoBagPrompt,
			CharacterThreeViewNoExternalObjectPrompt,
			"exactly one character only");
	}

	private static string BuildCharacterThreeViewIdentityFallbackPrompt(WorkflowNode node, CharacterDesignEntry entry)
	{
		return MergeCommaPromptFragments(
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			BuildEnglishCharacterGenderLock(entry, fullBody: true),
			BuildEnglishCharacterIdentityLock(entry),
			"clean character turnaround reference",
			"front view, side view, back view only",
			"complete full body visible from head to toe in every view",
			CharacterThreeViewFullBodyPrompt,
			"neutral standing pose",
			"empty hands",
			"same exact outfit, same shoes, same wearable clothing accessories except bags, same hairstyle, same body proportions",
			CharacterThreeViewCleanBackgroundPrompt,
			CharacterThreeViewNoBagPrompt,
			CharacterThreeViewNoExternalObjectPrompt,
			"no extra views, no text, no labels, no props");
	}

	private static string BuildEnglishCharacterGenderLock(CharacterDesignEntry entry, bool fullBody)
	{
		return CharacterPromptTextBuilder.DetectGender(entry) switch
		{
			CharacterGenderHint.Male => fullBody
				? "adult male only, masculine bone structure, masculine jawline, male body proportions, flat chest, broad male shoulder-to-neck ratio, no woman, no girl, no feminine face, no breasts, no skirt"
				: "adult male only, masculine facial structure, masculine jawline, stronger brow ridge, male hairline, same beard and facial hair if present, no woman, no girl, no feminine face, no feminine makeup, no long feminine eyelashes",
			CharacterGenderHint.Female => fullBody
				? "adult female only, feminine body proportions, feminine silhouette, no man, no boy, no masculine torso, no beard"
				: "adult female only, feminine facial structure, feminine facial proportions, no man, no boy, no masculine face, no beard",
			_ => "strictly preserve the source character gender and age, never change gender presentation"
		};
	}

	private static string BuildEnglishExpressionFacialHairLock(CharacterDesignEntry entry)
	{
		return CharacterPromptTextBuilder.DetectGender(entry) == CharacterGenderHint.Male
			? "if facial hair exists, keep exact same beard, mustache, stubble shape, length, density, placement, and color across all expression cells"
			: string.Empty;
	}

	private static string BuildExpressionIdentityNegativePrompt(CharacterDesignEntry entry)
	{
		string hairNegative = HasShortHairHint(entry)
			? "long hair, shoulder-length hair, changed short haircut"
			: string.Empty;

		return MergeCommaPromptFragments(
			"different identity, face swap, wrong gender, gender swap",
			"different face, changed haircut, changed hair color, changed hair length",
			"different clothes, different outfit, outfit switching",
			"extra head, duplicate face, multiple heads, face collage",
			"chest, bust, full body, hands visible, arms visible",
			"side face, profile view, turned head, three-quarter face",
			"unchanged expression, expressionless",
			"background, environment, scenery, room, street, objects",
			"sketch, line art, rough drawing, concept art",
			hairNegative);
	}

	private static string BuildThreeViewCellNegativePrompt()
	{
		return MergeCommaPromptFragments(
			"portrait, close-up, crop, headshot, bust, half body, cut off body or feet",
			"multiple views in one image, split composition, inset portrait, extra figure",
			"dynamic pose, walking, twisted torso, three-quarter view, fashion pose",
			"different clothes, redesigned outfit, changed costume",
			"background, environment, scenery, room, street, building, furniture",
			"bag, backpack, strap, carrying any bag",
			"microphone, camera, phone, weapon, tool, handheld prop, furniture, desk, chair");
	}

	private static string BuildThreeViewIdentityNegativePrompt(CharacterDesignEntry entry)
	{
		string genderNegative = CharacterPromptTextBuilder.DetectGender(entry) switch
		{
			CharacterGenderHint.Male => "female, woman, girl, feminine body, breasts, feminine makeup, lipstick, high heels, gender swap",
			CharacterGenderHint.Female => "male, man, boy, masculine body, beard, mustache, stubble, broad shoulders, gender swap",
			_ => "wrong gender, changed gender, gender swap"
		};

		return MergeCommaPromptFragments(
			genderNegative,
			"different person, different face, changed hairstyle, changed age",
			"different outfit, changed clothes, barefoot, missing shoes",
			"different accessory, changed color palette, redesigned costume",
			"background, environment, scenery, room, street",
			"bag, backpack, bag strap, carrying any bag",
			"microphone, camera, phone, weapon, tool, handheld prop, furniture, desk, chair");
	}

	private static string BuildLocalThreeViewViewNegativePrompt(int viewIndex)
	{
		return viewIndex switch
		{
			0 => "three-quarter view, angled body, side view, profile, back view, hidden eye",
			1 => "front view, back view, three-quarter, face visible, both eyes, chest facing camera, front torso visible",
			2 => "front view, side view, profile, three-quarter, face visible, eyes visible, body facing camera",
			_ => string.Empty
		};
	}

	private static bool HasShortHairHint(CharacterDesignEntry entry)
	{
		string text = string.Join(" ", new[]
		{
			entry.BasicStats,
			entry.AppearancePrompt,
			entry.CostumeNotes,
			entry.VisualTags,
			entry.ActingNotes,
			entry.Summary
		}.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

		return text.Contains("短发", StringComparison.Ordinal) ||
			text.Contains("利落", StringComparison.Ordinal) ||
			text.Contains("short hair", StringComparison.Ordinal) ||
			text.Contains("short haircut", StringComparison.Ordinal);
	}

	private static string BuildEnglishCharacterIdentityLock(CharacterDesignEntry entry)
	{
		var basicStats = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.BasicStats);
		var appearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
		var costumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);
		return MergeCommaPromptFragments(
			string.IsNullOrWhiteSpace(entry.Name) ? string.Empty : "character name: " + entry.Name,
			string.IsNullOrWhiteSpace(basicStats) ? string.Empty : "fixed basic stats: " + basicStats,
			string.IsNullOrWhiteSpace(appearancePrompt) ? string.Empty : "fixed appearance: " + appearancePrompt,
			string.IsNullOrWhiteSpace(costumeNotes) ? string.Empty : "fixed outfit: " + costumeNotes,
			"single exact outfit anchor only, no clothing alternatives, no outfit switching, keep the same visible collar and neckline",
			string.IsNullOrWhiteSpace(entry.VisualTags) ? string.Empty : "fixed visual tags: " + entry.VisualTags);
	}

	private static async Task<GeneratedArtifact> GenerateCharacterDesignArtifactAsync(WorkflowNode node, CharacterDesignEntry entry, ModelInfo? textToImageModel, ModelInfo? imageToImageModel, string prompt, string negativePrompt, CharacterDesignActionType action, CancellationToken cancellationToken)
	{
		string moduleName = ((action == CharacterDesignActionType.GenerateExpression) ? "角色设计/九宫格图片" : "角色设计/三视图图片");
		var (targetWidth, targetHeight) = GetCharacterDesignTargetSize(action);
		string targetSize = $"{targetWidth}x{targetHeight}";
		ModelInfo? imageModel = imageToImageModel ?? textToImageModel;
		ModelInfo? referenceImageModel = textToImageModel ?? imageModel;
		if (imageModel != null && !string.IsNullOrWhiteSpace(imageModel.Url))
		{
			if (IsComfyUiLike(imageModel.Url))
			{
				ModelInfo firstImageModel = referenceImageModel != null && IsComfyUiLike(referenceImageModel.Url)
					? referenceImageModel
					: imageModel;
				return (action == CharacterDesignActionType.GenerateExpression)
					? await GenerateComfyUiCharacterExpressionSheetAsync(firstImageModel, imageModel, node, entry, prompt, negativePrompt, cancellationToken, moduleName)
					: await GenerateComfyUiCharacterThreeViewSheetAsync(firstImageModel, imageModel, node, entry, prompt, negativePrompt, cancellationToken, moduleName);
			}
			if (IsStableDiffusionLike(imageModel.Url))
			{
				ModelInfo firstImageModel = referenceImageModel != null && IsStableDiffusionLike(referenceImageModel.Url)
					? referenceImageModel
					: imageModel;
				return (action == CharacterDesignActionType.GenerateExpression)
					? await GenerateStableDiffusionCharacterExpressionSheetAsync(firstImageModel, imageModel, node, entry, prompt, negativePrompt, cancellationToken, moduleName)
					: await GenerateStableDiffusionCharacterThreeViewSheetAsync(firstImageModel, imageModel, node, entry, prompt, negativePrompt, cancellationToken, moduleName);
			}
			if (IsYunWuLike(imageModel.Url))
			{
				string runtimePrompt = IsYunWuGeminiImageModel(imageModel.Id)
					? ((action == CharacterDesignActionType.GenerateExpression) ? CharacterPromptTextBuilder.BuildChineseExpressionPrompt(node, entry) : CharacterPromptTextBuilder.BuildChineseThreeViewPrompt(node, entry))
					: ((action == CharacterDesignActionType.GenerateExpression) ? BuildGptImageExpressionSheetPrompt(node, entry, prompt) : prompt);
				string faceReferencePath = ResolveCharacterFaceReferenceImagePath(node, entry);
				string[] referenceImages = string.IsNullOrWhiteSpace(faceReferencePath)
					? Array.Empty<string>()
					: new string[1] { faceReferencePath };
				if (action == CharacterDesignActionType.GenerateThreeView &&
					!string.IsNullOrWhiteSpace(faceReferencePath) &&
					SupportsConfiguredImageEdit(imageModel))
				{
					string editPrompt = BuildReferenceDrivenThreeViewPrompt(node, entry, runtimePrompt);
					return await ExecuteOpenAiCompatibleImageEditAsync(imageModel, node, editPrompt, faceReferencePath, cancellationToken, targetSize, moduleName);
				}
				return await ExecuteYunWuImageAsync(imageModel, node, runtimePrompt, string.Empty, cancellationToken, targetSize, moduleName, referenceImages);
			}
			if (IsGptImageModel(imageModel))
			{
				string runtimePrompt = BuildGptImageCharacterDesignPrompt(node, entry, prompt, action);
				string faceReferencePath = ResolveCharacterFaceReferenceImagePath(node, entry);
				if (action == CharacterDesignActionType.GenerateThreeView && !string.IsNullOrWhiteSpace(faceReferencePath))
				{
					return await ExecuteOpenAiCompatibleImageEditAsync(imageModel, node, runtimePrompt, faceReferencePath, cancellationToken, targetSize, moduleName);
				}
				return await ExecuteOpenAiCompatibleImageAsync(imageModel, node, runtimePrompt, cancellationToken, targetSize, moduleName);
			}
			if (action == CharacterDesignActionType.GenerateThreeView && SupportsConfiguredImageEdit(imageModel))
			{
				string faceReferencePath = ResolveCharacterFaceReferenceImagePath(node, entry);
				if (!string.IsNullOrWhiteSpace(faceReferencePath))
				{
					string editPrompt = BuildReferenceDrivenThreeViewPrompt(node, entry, prompt);
					return await ExecuteOpenAiCompatibleImageEditAsync(imageModel, node, editPrompt, faceReferencePath, cancellationToken, targetSize, moduleName);
				}
			}
			return await ExecuteOpenAiCompatibleImageAsync(imageModel, node, prompt, cancellationToken, targetSize, moduleName, ModelConfig.IsLocalEndpointUrl(imageModel.Url) ? negativePrompt : string.Empty);
		}
		string fallbackPath = SaveCharacterDesignBoard(node, entry, action, prompt);
		string description = ((action == CharacterDesignActionType.GenerateExpression) ? "未配置图像模型，已根据真实角色档案生成本地九宫格设计板。" : "未配置图像模型，已根据真实角色档案生成本地三视图设计板。");
		return new GeneratedArtifact(fallbackPath, description);
	}

	private static bool IsGptImageModel(ModelInfo model)
	{
		return ContainsGptImageToken(model.Id) || ContainsGptImageToken(model.Name);
	}

	private static void EnsureLocalOnlyModel(ModelInfo model, string modelLabel)
	{
		if (!ModelConfig.LocalOnlyMode)
		{
			return;
		}

		if (model == null || string.IsNullOrWhiteSpace(model.Url) || (!ModelConfig.IsLocalEndpointUrl(model.Url) && !ModelConfig.IsGeminiModelUrl(model.Url)))
		{
			throw new InvalidOperationException($"本地版只允许调用本机或局域网{modelLabel}。");
		}
	}

	private static bool ContainsGptImageToken(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		string normalized = value.Trim().ToLowerInvariant();
		return normalized.Contains("gpt-image", StringComparison.Ordinal) ||
			normalized.Contains("gpt image", StringComparison.Ordinal) ||
			normalized.Contains("gpt_image", StringComparison.Ordinal);
	}

	private static string BuildGptImageCharacterDesignPrompt(WorkflowNode node, CharacterDesignEntry entry, string identityPrompt, CharacterDesignActionType action)
	{
		return action == CharacterDesignActionType.GenerateExpression
			? BuildGptImageExpressionSheetPrompt(node, entry, identityPrompt)
			: BuildGptImageThreeViewPrompt(node, entry, identityPrompt);
	}

	private static string BuildGptImageExpressionSheetPrompt(WorkflowNode node, CharacterDesignEntry entry, string identityPrompt)
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("Create one final image: an exact 3x3 nine-panel expression sheet.");
		builder.AppendLine("The image must contain exactly nine separate equal square portrait panels, arranged in three columns and three rows, with thin clean gutters between panels.");
		builder.AppendLine("Every panel must show the same single character as an exact straight-on front close-up head-and-shoulders portrait, face parallel to the camera, head level, shoulder-up framing only, crop at the collarbone, no chest, no bust, no torso, no arms, no hands, centered, same camera distance, same seamless light gray or white studio background.");
		builder.AppendLine("Background lock: every expression panel must have a completely empty uniform studio backdrop only. Do not draw any workplace, scenery, vehicle, food truck, room, door, window, wall detail, sign, poster, shelf, furniture, plant, table, prop, or object behind the character.");
		builder.AppendLine("All nine panels must stay strict front-facing portraits. No side face, no profile view, no turned head, no head tilt, no back view, and no three-quarter angle.");
		builder.AppendLine("Each panel must show a clearly different readable expression at first glance. Never duplicate the same neutral expression across multiple panels.");
		builder.AppendLine("Do not create one large portrait. Do not create a single face. Do not leave any panel empty. Do not merge panels. Do not add text, numbers, labels, captions, logos, or watermarks.");
		builder.AppendLine("Use these nine expressions from left to right, top to bottom: " + CharacterPromptTextBuilder.BuildGptImageExpressionList(entry) + ".");
		builder.AppendLine("Keep the same identity in all nine panels: same face shape, same eyes, same eyebrows, same nose, same mouth shape, same hairstyle, same hairline, same bangs, same hair volume, same hair color, same eye color, same age, same gender, same visible clothing collar and top of shoulders only.");
		builder.AppendLine("Gender lock: " + BuildEnglishCharacterGenderLock(entry, fullBody: false) + ".");
		if (CharacterPromptTextBuilder.DetectGender(entry) == CharacterGenderHint.Male)
		{
			builder.AppendLine("For male characters, if facial hair exists, preserve the exact same beard, mustache, stubble shape, length, density, placement, and color in all nine panels; never add or remove facial hair between panels.");
		}
		builder.AppendLine($"Style: {WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node)}, production reference quality.");
		builder.AppendLine();
		AppendGptImageCharacterFacts(builder, entry);
		builder.AppendLine();
		builder.AppendLine("Character identity and style reference:");
		builder.AppendLine(identityPrompt.Trim());
		builder.AppendLine();
		builder.AppendLine("Important for this image request: if the identity reference mentions a single portrait, separate rendering, or avoiding grid layouts, ignore that layout instruction. The required final output here is exactly one complete 3x3 grid image with nine different expressions.");
		return builder.ToString().Trim();
	}

	private static string BuildGptImageThreeViewPrompt(WorkflowNode node, CharacterDesignEntry entry, string identityPrompt)
	{
		StringBuilder builder = new StringBuilder();
		builder.AppendLine("Create one final image: a clean character three-view turnaround reference sheet.");
		builder.AppendLine("Use the attached front-face expression portrait as the exact face identity, hairstyle, hairline, age, and gender reference; expand that face into a complete full-body character design.");
		builder.AppendLine("The image must contain exactly three full-body views of the same single character, arranged horizontally: front view on the left, side profile view in the center, back view on the right.");
		builder.AppendLine("Each of the three panels must itself be a full-body shot. The center panel is also a full-body side view, never a face portrait or zoomed-in bust.");
		builder.AppendLine("Use an orthographic character design reference style, neutral standing pose, complete full body visible from head to toe in every view, arms relaxed at sides, empty hands, seamless plain light gray or white studio background only.");
		builder.AppendLine("Background lock: the background must be completely empty and uniform in every panel. Do not draw any workplace, scenery, vehicle, food truck, room, door, window, wall detail, sign, poster, shelf, furniture, plant, table, prop, or object behind the character.");
		builder.AppendLine("No-bag lock: the character must not wear, carry, hold, or have any bag in any panel. Remove backpacks, shoulder bags, tote bags, handbags, purses, satchels, messenger bags, crossbody bags, sling bags, waist bags, pouches, luggage, bag straps, shoulder straps, crossbody straps, backpack straps, bag handles, and bag hardware even if the reference or character facts mention them.");
		builder.AppendLine("Only-person lock: only the character's body, hair, wearable clothing and shoes may appear. Do not draw any microphone, mic stand, camera, tripod, light stand, phone, tablet, weapon, tool, umbrella, staff, suitcase, box, paper, folder, clipboard, desk, chair, podium, cable, badge prop, handheld prop, floating prop, foreground prop, background prop, or profession equipment.");
		builder.AppendLine("View lock: the front view must be an exact straight-on full-body view with chest and hips facing the camera; the side view must be an exact 90-degree side profile full-body view; the back view must be an exact 180-degree back full-body view.");
		builder.AppendLine("Do not turn the torso, do not twist the hips, do not step forward, do not cross the legs, do not use a walking pose, do not use a fashion pose, and do not drift into any three-quarter angle.");
		builder.AppendLine("Mandatory full-body framing: include the complete head, torso, both legs, both feet, and shoes inside the image with a small margin around the head and feet.");
		builder.AppendLine("Do not create one single full-body portrait. Do not crop the body. Do not create a portrait, bust, half-body, waist-up, knee-up, cropped head, cropped feet, missing feet, or missing shoes. Do not add text, labels, numbers, captions, logos, or watermarks.");
		builder.AppendLine("Keep the same identity and costume across all three views: same hairstyle, same hair color, same body proportions, same outfit layers, same accessories, same shoes, same color palette.");
		builder.AppendLine("Gender lock: " + BuildEnglishCharacterGenderLock(entry, fullBody: true) + ".");
		builder.AppendLine($"Style: {WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node)}, production reference quality.");
		builder.AppendLine();
		AppendGptImageCharacterFacts(builder, entry);
		builder.AppendLine();
		builder.AppendLine("Character identity and style reference:");
		builder.AppendLine(identityPrompt.Trim());
		builder.AppendLine();
		builder.AppendLine("Important for this image request: if the identity reference mentions separate rendering or avoiding multi-view layouts, ignore that layout instruction. The required final output here is exactly one complete three-view turnaround sheet.");
		return builder.ToString().Trim();
	}

	private static string BuildReferenceDrivenThreeViewPrompt(WorkflowNode node, CharacterDesignEntry entry, string basePrompt)
	{
		return MergePromptParagraphs(
			"使用上传的正面面部表情参考图作为唯一脸部身份参考：保持同一张脸、同一发型、同一发际线、同一年龄、同一性别，并把该正面头像扩展成完整全身角色形象。",
			"生成一张干净的角色三视图设定板，只包含三个全身视图，从左到右依次为：正面、侧面、背面。每个视图必须从头顶到脚底完整可见，必须包含完整头部、躯干、双腿、双脚和鞋子，脚底不能被画面边缘裁切，正交参考图效果，中性站姿，双手自然下垂，空手，无道具，浅灰或白色纯色背景。",
			"背景必须完全干净统一：只允许浅灰或白色无缝影棚背景，禁止画任何工作场景、街景、车辆、餐车、房间、门窗、墙面细节、招牌、海报、货架、桌椅、植物、道具或背景物体。",
			"人物不能背任何包，也不能拎包、挎包或佩戴包带；禁止双肩包、单肩包、斜挎包、托特包、手提包、钱包、小挎包、腰包、行李、包带、肩带、斜挎带、背包带、包提手和包扣，即使参考图或角色设定里有包也必须移除。",
			"只允许出现人物本体、头发、身上穿着的衣服和鞋子。禁止任何外部物品：麦克风、话筒架、摄像机、三脚架、灯架、手机、平板、武器、工具、雨伞、手杖、行李箱、盒子、纸张、文件夹、桌子、椅子、讲台、电线、证件道具、手持物、漂浮物、前景物、背景物和职业设备都不能出现。",
			"三格中的每一格都必须是完整全身照，侧面格也必须是完整全身侧面，绝不允许任何一格变成脸部特写、胸像、半身像或放大头像。",
			"视角必须严格锁定：正面图必须胸口和胯部正对镜头；侧面图必须是严格 90 度纯侧身；背面图必须是严格 180 度纯背身。禁止斜身、迈步、扭胯、回头、三分之四视角、走姿和时装摆拍。",
			"三张视图必须保持同一套服装、鞋子、配饰、发型轮廓、身体比例和配色；不要生成九宫格、头像板、四分之三视图、额外小图、文字标签、水印或多余面板。",
			"严禁头像、胸像、半身、腰部以上、膝盖以上、裁头、裁脚或任何不完整身体构图。",
			basePrompt,
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptorChinese(node));
	}

	private static string MergePromptParagraphs(params string?[] prompts)
	{
		return string.Join(Environment.NewLine, prompts
			.Where(prompt => !string.IsNullOrWhiteSpace(prompt))
			.Select(prompt => prompt!.Trim()));
	}

	private static void AppendGptImageCharacterFacts(StringBuilder builder, CharacterDesignEntry entry)
	{
		AppendPromptLine(builder, "Character name", entry.Name);
		AppendPromptLine(builder, "Alias", entry.Alias);
		AppendPromptLine(builder, "Basic stats", entry.BasicStats);
		AppendPromptLine(builder, "Summary", entry.Summary);
		AppendPromptLine(builder, "Profession", entry.Profession);
		AppendPromptLine(builder, "Personality", entry.Personality);
		AppendPromptLine(builder, "Appearance", entry.AppearancePrompt);
		AppendPromptLine(builder, "Costume", entry.CostumeNotes);
		AppendPromptLine(builder, "Visual tags", entry.VisualTags);
	}

	private static async Task<string> BuildLocalCharacterNegativePromptAsync(
		ModelInfo textModel,
		WorkflowNode node,
		CharacterDesignEntry entry,
		string positivePrompt,
		string baseNegativePrompt,
		CharacterDesignActionType action,
		CancellationToken cancellationToken)
	{
		string moduleName = action == CharacterDesignActionType.GenerateExpression
			? "角色设计/九宫格反向提示词"
			: "角色设计/三视图反向提示词";
		try
		{
			string request = BuildCharacterNegativePromptRequest(entry, positivePrompt, baseNegativePrompt, action);
			string generated = WorkflowExecutor.NormalizeTextResult(
				result: await ExecuteTextCompletionAsync(textModel, request, cancellationToken, 0.2, null, moduleName),
				nodeType: node.Type);
			return MergeCommaPromptFragments(baseNegativePrompt, generated);
		}
		catch
		{
			return baseNegativePrompt;
		}
	}

	private static string BuildCharacterNegativePromptRequest(
		CharacterDesignEntry entry,
		string positivePrompt,
		string baseNegativePrompt,
		CharacterDesignActionType action)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("You are a negative prompt engineer for anime image models. Return ONE English negative prompt. No JSON, no numbering. Use comma-separated tags.");
		sb.AppendLine();
		if (action == CharacterDesignActionType.GenerateExpression)
		{
			sb.AppendLine("Task: 3x3 anime expression sheet, close-up face cells. Exclude: full body, bust, torso, arms, hands, props, wrong face/hairstyle/gender, multiple characters, text, watermark, grid artifacts, environment background.");
		}
		else
		{
			sb.AppendLine("Task: full-body anime turnaround (front/side/back). Exclude: close-up, bust, half-body, waist-up, cropped body/feet/head, inconsistent outfit, handheld props, multiple people, text, watermark, environment background, bags, accessories, equipment.");
		}
		sb.AppendLine();
		sb.AppendLine("Character facts:");
		AppendPromptLine(sb, "name", entry.Name);
		AppendPromptLine(sb, "basic stats", entry.BasicStats);
		AppendPromptLine(sb, "summary", entry.Summary);
		AppendPromptLine(sb, "appearance", entry.AppearancePrompt);
		AppendPromptLine(sb, "costume", entry.CostumeNotes);
		AppendPromptLine(sb, "visual tags", entry.VisualTags);
		sb.AppendLine();
		sb.AppendLine("Positive prompt:");
		sb.AppendLine(positivePrompt.Trim());
		sb.AppendLine();
		sb.AppendLine("Base negative prompt:");
		sb.AppendLine(baseNegativePrompt.Trim());
		return sb.ToString().Trim();
	}

	private static void AppendPromptLine(StringBuilder builder, string title, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			builder.AppendLine($"{title}: {value.Trim()}");
		}
	}

	private static string MergeCommaPromptFragments(params string?[] prompts)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> parts = new List<string>();
		foreach (string? prompt in prompts)
		{
			if (string.IsNullOrWhiteSpace(prompt))
			{
				continue;
			}

			foreach (string part in prompt
				.Replace("\r\n", ",", StringComparison.Ordinal)
				.Replace('\n', ',')
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				string cleaned = part.Trim().Trim('.', ';', '，', '。');
				if (!string.IsNullOrWhiteSpace(cleaned) && seen.Add(cleaned))
				{
					parts.Add(cleaned);
				}
			}
		}

		return string.Join(", ", parts);
	}

	private static string RemovePromptFragments(string prompt, params string[] blockedPhrases)
	{
		if (string.IsNullOrWhiteSpace(prompt))
		{
			return string.Empty;
		}

		List<string> parts = new List<string>();
		foreach (string part in prompt
			.Replace("\r\n", ",", StringComparison.Ordinal)
			.Replace('\n', ',')
			.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string cleaned = part.Trim().Trim('.', ';', '，', '。');
			if (string.IsNullOrWhiteSpace(cleaned))
			{
				continue;
			}

			string normalized = cleaned.ToLowerInvariant();
			if (blockedPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			parts.Add(cleaned);
		}

		return string.Join(", ", parts);
	}

	private static string ResolveCharacterFaceReferenceImagePath(WorkflowNode node, CharacterDesignEntry entry)
	{
		string expressionFrontReference = TryCreateExpressionFrontFaceReference(node, entry);
		if (!string.IsNullOrWhiteSpace(expressionFrontReference))
		{
			entry.ReferencePortraitPath = expressionFrontReference;
			return expressionFrontReference;
		}

		if (!string.IsNullOrWhiteSpace(entry.ReferencePortraitPath) && File.Exists(entry.ReferencePortraitPath))
		{
			return entry.ReferencePortraitPath;
		}

		return string.Empty;
	}

	private static string TryCreateExpressionFrontFaceReference(WorkflowNode node, CharacterDesignEntry entry)
	{
		if (string.IsNullOrWhiteSpace(entry.ExpressionSheetPath) || !File.Exists(entry.ExpressionSheetPath))
		{
			return string.Empty;
		}

		try
		{
			using Bitmap source = new Bitmap(entry.ExpressionSheetPath);
			Rectangle cropRect = ResolveExpressionFrontCellRect(source);
			if (cropRect.Width < 64 || cropRect.Height < 64)
			{
				return string.Empty;
			}

			string outputDirectory = EnsureOutputDirectory(node.Type);
			string sourceStamp = File.GetLastWriteTimeUtc(entry.ExpressionSheetPath).Ticks.ToString(CultureInfo.InvariantCulture);
			string outputPath = Path.Combine(outputDirectory, $"{node.Id}_{SanitizeFileSegment(entry.Name)}_front_face_reference_{sourceStamp}.png");
			if (File.Exists(outputPath))
			{
				return outputPath;
			}

			using Bitmap cropped = new Bitmap(cropRect.Width, cropRect.Height);
			using (Graphics graphics = Graphics.FromImage(cropped))
			{
				graphics.Clear(Color.FromArgb(242, 242, 242));
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
				graphics.DrawImage(source, new Rectangle(0, 0, cropped.Width, cropped.Height), cropRect, GraphicsUnit.Pixel);
			}

			cropped.Save(outputPath, ImageFormat.Png);
			return outputPath;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string CreateThreeViewFrontSeedCanvas(WorkflowNode node, CharacterDesignEntry entry, string faceReferencePath)
	{
		if (string.IsNullOrWhiteSpace(faceReferencePath) || !File.Exists(faceReferencePath))
		{
			return string.Empty;
		}

		try
		{
			string outputDirectory = EnsureOutputDirectory(node.Type);
			string sourceStamp = File.GetLastWriteTimeUtc(faceReferencePath).Ticks.ToString(CultureInfo.InvariantCulture);
			string outputPath = Path.Combine(outputDirectory, $"{node.Id}_{SanitizeFileSegment(entry.Name)}_threeview_front_seed_{sourceStamp}.png");
			if (File.Exists(outputPath))
			{
				return outputPath;
			}

			using Bitmap source = new Bitmap(faceReferencePath);
			using Bitmap canvas = new Bitmap(512, 1024);
			using Graphics graphics = Graphics.FromImage(canvas);
			graphics.Clear(Color.FromArgb(242, 242, 242));
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

			int targetFaceWidth = 180;
			int targetFaceHeight = Math.Max(180, (int)Math.Round(source.Height * (targetFaceWidth / (double)source.Width)));
			if (targetFaceHeight > 240)
			{
				double scale = 240d / targetFaceHeight;
				targetFaceWidth = Math.Max(120, (int)Math.Round(targetFaceWidth * scale));
				targetFaceHeight = 240;
			}

			int faceX = (canvas.Width - targetFaceWidth) / 2;
			int faceY = 72;
			graphics.DrawImage(source, new Rectangle(faceX, faceY, targetFaceWidth, targetFaceHeight));
			canvas.Save(outputPath, ImageFormat.Png);
			return outputPath;
		}
		catch
		{
			return faceReferencePath;
		}
	}

	private static Rectangle ResolveExpressionFrontCellRect(Bitmap source)
	{
		if (source.Width < 600 || source.Height < 600)
		{
			return Rectangle.Empty;
		}

		int boardContentWidth = source.Width - 56 - 48;
		int boardContentHeight = source.Height - 56 - 48;
		if (boardContentWidth > 0 &&
			boardContentHeight > 0 &&
			boardContentWidth % 3 == 0 &&
			boardContentHeight % 3 == 0)
		{
			int cellWidth = boardContentWidth / 3;
			int cellHeight = boardContentHeight / 3;
			int insetX = Math.Max(12, cellWidth / 14);
			int insetY = Math.Max(12, cellHeight / 14);
			return new Rectangle(28 + insetX, 28 + insetY, cellWidth - insetX * 2, cellHeight - insetY * 2);
		}

		int fallbackWidth = Math.Max(64, source.Width / 3);
		int fallbackHeight = Math.Max(64, source.Height / 3);
		int fallbackInsetX = Math.Max(8, fallbackWidth / 16);
		int fallbackInsetY = Math.Max(8, fallbackHeight / 16);
		return new Rectangle(fallbackInsetX, fallbackInsetY, fallbackWidth - fallbackInsetX * 2, fallbackHeight - fallbackInsetY * 2);
	}

	private static (int Width, int Height) GetCharacterDesignTargetSize(CharacterDesignActionType action)
	{
		return action == CharacterDesignActionType.GenerateThreeView
			? (Width: 1376, Height: 768)
			: (Width: 1024, Height: 1024);
	}

	private static string GetCharacterDesignModuleName(CharacterDesignActionType action)
	{
		bool flag = false;
		if (1 == 0)
		{
		}
		string text = action switch
		{
			CharacterDesignActionType.GenerateProfile => "角色设计/角色档案", 
			CharacterDesignActionType.GenerateExpression => "角色设计/九宫格图片", 
			CharacterDesignActionType.GenerateThreeView => "角色设计/三视图图片", 
			_ => "角色设计", 
		};
		if (1 == 0)
		{
		}
		string result = text;
		bool flag2 = false;
		return result;
	}

	private static string SaveCharacterDesignBoard(WorkflowNode node, CharacterDesignEntry entry, CharacterDesignActionType action, string prompt)
	{
		int num = ((action == CharacterDesignActionType.GenerateExpression) ? 1320 : 1440);
		int num2 = ((action == CharacterDesignActionType.GenerateExpression) ? 1320 : 960);
		using Bitmap bitmap = new Bitmap(num, num2);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.Clear(Color.FromArgb(17, 18, 24));
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using SolidBrush brush = new SolidBrush(Color.FromArgb(26, 28, 36));
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(34, 37, 46));
		using SolidBrush brush3 = new SolidBrush((action == CharacterDesignActionType.GenerateExpression) ? Color.FromArgb(255, 122, 0) : Color.FromArgb(46, 120, 255));
		using Pen pen = new Pen(Color.FromArgb(88, 104, 124), 2f);
		using Font font = new Font("Microsoft YaHei", 34f, FontStyle.Bold, GraphicsUnit.Pixel);
		using Font font2 = new Font("Microsoft YaHei", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
		using Font font3 = new Font("Microsoft YaHei", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
		using Font font4 = new Font("Microsoft YaHei", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
		using SolidBrush brush4 = new SolidBrush(Color.WhiteSmoke);
		using SolidBrush brush5 = new SolidBrush(Color.FromArgb(188, 198, 214));
		graphics.FillRectangle(brush3, new Rectangle(0, 0, num, 18));
		graphics.FillRectangle(brush, new Rectangle(36, 42, num - 72, num2 - 78));
		graphics.DrawString(entry.Name, font, brush4, new RectangleF(62f, 66f, num - 124, 46f));
		graphics.DrawString((action == CharacterDesignActionType.GenerateExpression) ? "角色九宫格设计板" : "角色三视图设计板", font2, brush3, new RectangleF(64f, 116f, num - 128, 30f));
		graphics.DrawString(string.IsNullOrWhiteSpace(entry.CompactSummary) ? "根据角色档案自动整理生成" : entry.CompactSummary, font3, brush5, new RectangleF(64f, 148f, num - 128, 56f));
		if (action == CharacterDesignActionType.GenerateExpression)
		{
			string[] array = new string[9] { "平静", "微笑", "惊讶", "愤怒", "委屈", "坚定", "狡黠", "害羞", "哭泣" };
			int num3 = 64;
			int num4 = 230;
			int num5 = 280;
			int num6 = 18;
			for (int i = 0; i < array.Length; i++)
			{
				int num7 = i / 3;
				int num8 = i % 3;
				Rectangle rect = new Rectangle(num3 + num8 * (num5 + num6), num4 + num7 * (num5 + num6), num5, num5);
				graphics.FillRectangle(brush2, rect);
				graphics.DrawRectangle(pen, rect);
				graphics.DrawString(array[i], font2, brush3, new RectangleF(rect.X + 16, rect.Y + 16, rect.Width - 32, 28f));
				graphics.DrawString(entry.Name, font3, brush4, new RectangleF(rect.X + 16, rect.Y + 58, rect.Width - 32, 24f));
				graphics.DrawString(string.IsNullOrWhiteSpace(entry.VisualTags) ? entry.RoleType : entry.VisualTags, font4, brush5, new RectangleF(rect.X + 16, rect.Bottom - 72, rect.Width - 32, 52f));
			}
		}
		else
		{
			string[] array2 = new string[3] { "正视图", "侧视图", "背视图" };
			int width = 398;
			int height = 520;
			int num9 = 78;
			for (int j = 0; j < array2.Length; j++)
			{
				Rectangle rect2 = new Rectangle(num9 + j * 430, 248, width, height);
				graphics.FillRectangle(brush2, rect2);
				graphics.DrawRectangle(pen, rect2);
				graphics.DrawString(array2[j], font2, brush3, new RectangleF(rect2.X + 20, rect2.Y + 18, rect2.Width - 40, 28f));
				graphics.DrawString(entry.Name, font3, brush4, new RectangleF(rect2.X + 20, rect2.Y + 58, rect2.Width - 40, 24f));
				graphics.DrawString(string.IsNullOrWhiteSpace(entry.CostumeNotes) ? entry.CompactSummary : entry.CostumeNotes, font4, brush5, new RectangleF(rect2.X + 20, rect2.Bottom - 110, rect2.Width - 40, 92f));
			}
		}
		graphics.DrawString(TrimForSummary(prompt, 320), font4, brush5, new RectangleF(64f, num2 - 118, num - 128, 72f));
		graphics.DrawString("JSAI Character Design Board", font4, brush3, new RectangleF(64f, num2 - 42, 280f, 22f));
		string path = EnsureOutputDirectory(node.Type);
		string path2 = $"{node.Id}_{SanitizeFileSegment(entry.Name)}_{((action == CharacterDesignActionType.GenerateExpression) ? "expression" : "threeview")}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
		string text = Path.Combine(path, path2);
		bitmap.Save(text, ImageFormat.Png);
		return text;
	}

	private static string ReadJsonProperty(JsonElement element, string propertyName)
	{
		if (!TryGetJsonProperty(element, propertyName, out JsonElement value))
		{
			return string.Empty;
		}

		return ReadJsonElementAsText(value);
	}

	private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
	{
		value = default;
		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		string normalizedName = NormalizeJsonPropertyName(propertyName);
		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(NormalizeJsonPropertyName(property.Name), normalizedName, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		return false;
	}

	private static string NormalizeJsonPropertyName(string value)
	{
		return new string((value ?? string.Empty)
			.Where(char.IsLetterOrDigit)
			.Select(char.ToLowerInvariant)
			.ToArray());
	}

	private static string ReadJsonElementAsText(JsonElement value)
	{
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
			JsonValueKind.Number => value.ToString(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
				.Select(ReadJsonElementAsText)
				.Where(item => !string.IsNullOrWhiteSpace(item))),
			JsonValueKind.Object => string.Join("; ", value.EnumerateObject()
				.Select(property => $"{property.Name}: {ReadJsonElementAsText(property.Value)}")
				.Where(item => !string.IsNullOrWhiteSpace(item))),
			_ => string.Empty,
		};
	}

	private static string SanitizeFileSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "character";
		}
		char[] invalidChars = Path.GetInvalidFileNameChars();
		string text = new string(value.Select((char ch) => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
		return string.IsNullOrWhiteSpace(text) ? "character" : text;
	}

	private static string BuildVideoCollectionPlan(WorkflowDocument document, WorkflowNode node, string input, string reason)
	{
		List<WorkflowNode> list = WorkflowExecutor.CollectUpstreamNodes(document, node);
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# 视频合集方案");
		stringBuilder.AppendLine();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("- 节点：");
		handler.AppendFormatted(node.Type);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
		handler.AppendLiteral("- 生成时间：");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("- 说明：");
		handler.AppendFormatted(reason);
		stringBuilder5.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (!string.IsNullOrWhiteSpace(input))
		{
			stringBuilder.AppendLine("## 上游文本摘要");
			stringBuilder.AppendLine(TrimForSummary(input, 600));
			stringBuilder.AppendLine();
		}
		if (list.Count == 0)
		{
			stringBuilder.AppendLine("## 素材状态");
			stringBuilder.AppendLine("当前没有可用的上游视频或分镜资产。");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("## 建议");
			stringBuilder.AppendLine("1. 先执行“分镜视频”节点，生成详细分镜 JSON 或视频产物。");
			stringBuilder.AppendLine("2. 再回到“视频合集”节点生成最终合集方案。");
			return stringBuilder.ToString().Trim();
		}
		stringBuilder.AppendLine("## 上游素材");
		foreach (var item in list.Select((WorkflowNode value, int index) => (value: value, index: index)))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, stringBuilder2);
			handler.AppendFormatted(item.index + 1);
			handler.AppendLiteral(". ");
			handler.AppendFormatted(item.value.Type);
			stringBuilder6.AppendLine(ref handler);
			if (!string.IsNullOrWhiteSpace(item.value.ArtifactPath))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder7 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder2);
				handler.AppendLiteral("   - 文件：");
				handler.AppendFormatted(item.value.ArtifactPath);
				stringBuilder7.AppendLine(ref handler);
			}
			if (!string.IsNullOrWhiteSpace(item.value.Output))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder8 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder2);
				handler.AppendLiteral("   - 摘要：");
				handler.AppendFormatted(TrimForSummary(item.value.Output, 160));
				stringBuilder8.AppendLine(ref handler);
			}
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## 剪辑顺序建议");
		foreach (var item2 in list.Select((WorkflowNode value, int index) => (value: value, index: index)))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 3, stringBuilder2);
			handler.AppendFormatted(item2.index + 1);
			handler.AppendLiteral(". 先使用 ");
			handler.AppendFormatted(item2.value.Type);
			handler.AppendLiteral(" 的核心画面或关键段落作为第 ");
			handler.AppendFormatted(item2.index + 1);
			handler.AppendLiteral(" 段素材。");
			stringBuilder9.AppendLine(ref handler);
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("## 输出建议");
		stringBuilder.AppendLine("- 片头 2-3 秒快速建立主冲突。");
		stringBuilder.AppendLine("- 中段按剧情推进和情绪升级排列。");
		stringBuilder.AppendLine("- 结尾停在反转信息、人物反应或关键悬念上。");
		return stringBuilder.ToString().Trim();
	}

	private static async Task<GeneratedArtifact> ExecuteImageNodeAsync(ModelInfo model, WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		if (node.Type == "分镜图片")
		{
			return await ExecuteStoryboardImageGridAsync(model, document, node, input, cancellationToken);
		}
		string prompt = WorkflowExecutor.BuildImagePrompt(node, input);
		var (imageWidth, imageHeight) = GetImageCanvasSize(node);
		if (IsComfyUiLike(model.Url))
		{
			string negativePrompt = WorkflowExecutor.BuildImageNegativePrompt(node, input);
			return await ExecuteComfyUiImageAsync(model, node, prompt, negativePrompt, cancellationToken, imageWidth, imageHeight);
		}
		if (IsStableDiffusionLike(model.Url))
		{
			return await ExecuteStableDiffusionImageAsync(model, node, prompt, cancellationToken);
		}
		if (IsYunWuLike(model.Url))
		{
			return await ExecuteYunWuImageAsync(model, node, prompt, string.Empty, cancellationToken);
		}
		return await ExecuteOpenAiCompatibleImageAsync(model, node, prompt, cancellationToken);
	}

	private static async Task<GeneratedArtifact> ExecuteStoryboardImageGridAsync(ModelInfo model, WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		if (node.Params == null)
		{
			node.Params = new WorkflowNodeParameters();
		}
		node.Params.EnsureDefaults(node.Type);
		ApplyUpstreamOutlineVisualStyle(document, node);
		List<StoryboardShot> shots = WorkflowExecutor.CollectStoryboardShots(document, node, 96);
		if (shots.Count == 0)
		{
			throw new InvalidOperationException("请先连接“分镜图拆解”或“创意描述”节点，并确保上游已经产出可解析的分镜内容。");
		}
		node.Params.StoryboardShots = shots.Select((StoryboardShot shot) => shot.Clone()).ToList();
		int columns = (string.Equals(node.Params.StoryboardGridLayout, "2x3", StringComparison.Ordinal) ? 2 : 3);
		int shotsPerPage = columns * 3;
		int pageCount = (int)Math.Ceiling((double)shots.Count / (double)shotsPerPage);
		string upstreamText = WorkflowExecutor.CollectUpstreamOutput(document, node);
		(int Width, int Height) storyboardPanelCanvasSize = GetStoryboardPanelCanvasSize(node);
		int panelWidth = storyboardPanelCanvasSize.Width;
		int panelHeight = storyboardPanelCanvasSize.Height;
		List<string> generatedPages = new List<string>();
		ModelSettings settings = ModelConfig.Load();
		ModelInfo textToImageModel = ResolveStoryboardTextToImageModel(settings, node) ?? model;
		ModelInfo imageToImageModel = ResolveStoryboardImageToImageModel(settings, node) ?? textToImageModel;
		int textToImagePanelCount = 0;
		int imageToImagePanelCount = 0;
		for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
		{
			List<StoryboardShot> pageShots = shots.Skip(pageIndex * shotsPerPage).Take(shotsPerPage).ToList();
			List<string> generatedPanelPaths = new List<string>(pageShots.Count);
			foreach (StoryboardShot pageShot in pageShots)
			{
				List<CharacterDesignEntry> shotCharacterEntries = WorkflowExecutor.CollectStoryboardCharacterEntries(document, node, new[] { pageShot });
				IReadOnlyList<string>? referenceImagePaths = CollectStoryboardReferenceImages(shotCharacterEntries);
				bool hasReferenceImage = referenceImagePaths != null && referenceImagePaths.Count > 0;
				ModelInfo panelModel = hasReferenceImage ? imageToImageModel : textToImageModel;
				if (hasReferenceImage)
				{
					imageToImagePanelCount++;
				}
				else
				{
					textToImagePanelCount++;
				}

				generatedPanelPaths.Add((await ExecuteStoryboardPanelImageAsync(seed: BuildStableSeed(node.Id, pageIndex.ToString(), pageShot.Id, pageShot.Scene, pageShot.VisualDescription, pageShot.CharactersDisplay, Math.Max(1, pageShot.ShotNumber).ToString()), model: panelModel, node: node, prompt: WorkflowExecutor.BuildStoryboardPanelPrompt(document, node, pageShot, upstreamText), negativePrompt: WorkflowExecutor.BuildStoryboardPanelNegativePrompt(document, node, upstreamText), cancellationToken: cancellationToken, width: panelWidth, height: panelHeight, filePrefix: $"{node.Id}_storyboard_p{pageIndex + 1}_shot_{Math.Max(1, pageShot.ShotNumber):D2}", moduleName: hasReferenceImage ? "分镜图片/单格分镜图-图生图" : "分镜图片/单格分镜图-文生图", referenceImagePaths: referenceImagePaths)).Path);
			}
			string pagePath = ComposeGeneratedImageBoard(
				node,
				generatedPanelPaths,
				columns,
				3,
				$"{node.Id}_storyboard_page_{pageIndex + 1}",
				subtitles: BuildStoryboardPanelSubtitleLabels(pageShots));
			DeleteIntermediateImages(generatedPanelPaths);
			generatedPages.Add(pagePath);
		}
		node.Params.StoryboardGridPagePaths = generatedPages.ToList();
		node.Params.StoryboardCurrentPage = 0;
		node.Params.StoryboardTotalPages = generatedPages.Count;
		try
		{
			ProjectLibraryExportService.ExportStoryboardNodeToProjectFolder(document.ProjectName, node);
		}
		catch
		{
		}
		string firstPage = generatedPages.First();
		string description = $"已按分镜列表生成 {shots.Count} 格画面，共 {generatedPages.Count} 页。{Environment.NewLine}当前布局：{((columns == 2) ? "六宫格 (2x3)" : "九宫格 (3x3)")} / {((node.Params.StoryboardPanelOrientation == "9:16") ? "竖屏" : "横屏")}{Environment.NewLine}模型路由：文生图 {textToImagePanelCount} 格 / 图生图 {imageToImagePanelCount} 格。";
		return new GeneratedArtifact(firstPage, description);
	}

	private static async Task<GeneratedArtifact> ExecuteStoryboardPanelImageAsync(ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, CancellationToken cancellationToken, int width, int height, string filePrefix, long seed, string? moduleName = null, IReadOnlyList<string>? referenceImagePaths = null)
	{
		string? primaryReferenceImagePath = referenceImagePaths?
			.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
		if (IsComfyUiLike(model.Url))
		{
			return await ExecuteComfyUiImageAsync(model, node, prompt, negativePrompt, cancellationToken, width, height, filePrefix, seed, moduleName, referenceImagePaths);
		}
		if (IsStableDiffusionLike(model.Url))
		{
			if (!string.IsNullOrWhiteSpace(primaryReferenceImagePath))
			{
				return await ExecuteStableDiffusionImageToImageAsync(model, node, prompt, primaryReferenceImagePath, cancellationToken, width, height, negativePrompt, moduleName, seed, 0.72);
			}

			return await ExecuteStableDiffusionImageAsync(model, node, prompt, cancellationToken, width, height, negativePrompt, moduleName);
		}
		if (IsYunWuLike(model.Url))
		{
			return await ExecuteYunWuImageAsync(model, node, prompt, negativePrompt, cancellationToken, $"{width}x{height}", moduleName, referenceImagePaths);
		}
		if (!string.IsNullOrWhiteSpace(primaryReferenceImagePath) && SupportsConfiguredImageEdit(model))
		{
			return await ExecuteOpenAiCompatibleImageEditAsync(model, node, prompt, primaryReferenceImagePath, cancellationToken, $"{width}x{height}", moduleName);
		}

		return await ExecuteOpenAiCompatibleImageAsync(model, node, prompt, cancellationToken, $"{width}x{height}", moduleName, ModelConfig.IsLocalEndpointUrl(model.Url) ? negativePrompt : string.Empty);
	}

	private static (int Width, int Height) GetImageCanvasSize(WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
			WorkflowNodeParameters workflowNodeParameters3 = workflowNodeParameters;
		}
		node.Params.EnsureDefaults(node.Type);
		if (node.Type == "分镜图片")
		{
			return string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal) ? (Width: 1080, Height: 1920) : (Width: 1920, Height: 1080);
		}
		return (Width: 1024, Height: 1536);
	}

	private static (int Width, int Height) GetStoryboardPanelCanvasSize(WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
			WorkflowNodeParameters workflowNodeParameters3 = workflowNodeParameters;
		}
		node.Params.EnsureDefaults(node.Type);
		return string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal) ? (Width: 1080, Height: 1920) : (Width: 1920, Height: 1080);
	}

	private static (int Width, int Height) GetStoryboardGridCanvasSize(WorkflowNode node)
	{
		if (node.Params == null)
		{
			WorkflowNodeParameters workflowNodeParameters = (node.Params = new WorkflowNodeParameters());
			WorkflowNodeParameters workflowNodeParameters3 = workflowNodeParameters;
		}
		node.Params.EnsureDefaults(node.Type);
		int num = (string.Equals(node.Params.StoryboardGridLayout, "2x3", StringComparison.Ordinal) ? 2 : 3);
		if (string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal))
		{
			return (Width: num * 1080, Height: 5760);
		}
		return (Width: num * 1920, Height: 3240);
	}

	private static string GetImageCanvasSizeText(WorkflowNode node)
	{
		var (value, value2) = GetImageCanvasSize(node);
		return $"{value}x{value2}";
	}

	private static async Task<GeneratedArtifact> ExecuteComfyUiImageAsync(ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, CancellationToken cancellationToken, int width, int height, string? filePrefix = null, long? seed = null, string? moduleName = null, IReadOnlyList<string>? referenceImagePaths = null)
	{
		EnsureLocalOnlyModel(model, "ComfyUI 图片模型");
		PublishPrompt(moduleName ?? node.Type + "/ComfyUI图片", model, prompt, negativePrompt);
		try
		{
			string baseUrl = NormalizeComfyUiBaseUrl(model.Url);
			string? uploadedReferenceImageName = await UploadComfyUiReferenceImageAsync(baseUrl, referenceImagePaths, cancellationToken);
			string? workflowTemplateFileName = ResolveConfiguredComfyUiWorkflowTemplateFileName(model, !string.IsNullOrWhiteSpace(uploadedReferenceImageName));
			JsonObject workflow = LoadComfyUiWorkflowTemplate(workflowTemplateFileName, !string.IsNullOrWhiteSpace(uploadedReferenceImageName));
			ConfigureComfyUiWorkflow(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName, workflowTemplateFileName);
			string baseUrl2 = baseUrl;
			string baseUrl3 = baseUrl;
			string filePath = await DownloadComfyUiImageAsync(node, baseUrl2, await WaitForComfyUiImageAsync(baseUrl3, await SubmitComfyUiPromptAsync(baseUrl, workflow, cancellationToken), cancellationToken), cancellationToken);
			ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, null);
			return new GeneratedArtifact(filePath, "已通过 ComfyUI 生成图片文件，可继续用于下游节点。");
		}
		catch (Exception ex) when (IsComfyUiTransientTransportException(ex) || ex.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("ComfyUI 在返回图片时提前断开连接，已自动重试多次但仍失败。请稍后重试，或检查本地 ComfyUI 是否负载过高。", ex);
		}
	}

	private static async Task<GeneratedArtifact> GenerateComfyUiCharacterExpressionSheetAsync(ModelInfo textToImageModel, ModelInfo imageToImageModel, WorkflowNode node, CharacterDesignEntry entry, string basePrompt, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null)
	{
		IReadOnlyList<CharacterPromptTextBuilder.ExpressionCell> expressions = CharacterPromptTextBuilder.GetExpressions(entry);
		string expressionNegativePrompt = MergeCommaPromptFragments(WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry), negativePrompt, BuildExpressionIdentityNegativePrompt(entry));
		string referenceNegativePrompt = MergeCommaPromptFragments(WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry), negativePrompt);
		string referencePortraitPath = await EnsureComfyUiCharacterReferencePortraitAsync(textToImageModel, node, entry, basePrompt, referenceNegativePrompt, cancellationToken, moduleName, useSimpleReferencePrompt: true);
		List<string> filePaths = new List<string>(expressions.Count)
		{
			referencePortraitPath
		};
		for (int index = 1; index < expressions.Count; index++)
		{
			long promptSeed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "expression-cell", index.ToString());
			string prompt = BuildLocalExpressionCellPrompt(node, entry, BuildLocalExpressionTargetPrompt(expressions[index].EnglishLabel));
			List<string> list = filePaths;
			list.Add((await ExecuteComfyUiImageAsync(imageToImageModel, node, prompt, expressionNegativePrompt, cancellationToken, 512, 512, $"{node.Id}_{SanitizeFileSegment(entry.Name)}_expression_{index + 1}", promptSeed, moduleName, BuildReferenceImageList(referencePortraitPath))).Path);
		}
		string boardPath = ComposeGeneratedImageBoard(node, filePaths, 3, 3, node.Id + "_" + SanitizeFileSegment(entry.Name) + "_expression_sheet", BuildExpressionBoardLabels(expressions));
		DeleteIntermediateImages(filePaths.Where((string path) => !string.Equals(path, referencePortraitPath, StringComparison.OrdinalIgnoreCase)).ToList());
		return new GeneratedArtifact(boardPath, "已通过 ComfyUI 生成角色九宫格表情板。");
	}

	private static async Task<GeneratedArtifact> GenerateComfyUiCharacterThreeViewSheetAsync(ModelInfo textToImageModel, ModelInfo imageToImageModel, WorkflowNode node, CharacterDesignEntry entry, string basePrompt, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null)
	{
		string[] viewPrompts = new string[3]
		{
			"STRICT FRONT FULL-BODY VIEW ONLY, exact straight-on orthographic front view, chest facing camera, hips facing camera, both eyes visible, both shoulders even, no side angle",
			"STRICT LEFT SIDE FULL-BODY VIEW ONLY, exact 90-degree profile, body facing left, head aligned with body facing left, " + CharacterThreeViewStrictSidePrompt,
			"STRICT BACK FULL-BODY VIEW ONLY, exact 180-degree back view, face fully hidden, no eyes visible, no nose visible, no mouth visible, back of head visible, back of outfit visible, back of shoes visible, both shoulders seen from behind"
		};
		string effectiveNegativePrompt = MergeCommaPromptFragments(negativePrompt, BuildThreeViewCellNegativePrompt(), BuildThreeViewIdentityNegativePrompt(entry));
		string faceReferencePath = ResolveCharacterFaceReferenceImagePath(node, entry);
		if (string.IsNullOrWhiteSpace(faceReferencePath))
		{
			faceReferencePath = await EnsureComfyUiCharacterReferencePortraitAsync(textToImageModel, node, entry, basePrompt, negativePrompt, cancellationToken, moduleName);
		}
		string frontReferencePath = await EnsureComfyUiCharacterFrontThreeViewReferenceAsync(imageToImageModel, node, entry, faceReferencePath, effectiveNegativePrompt, cancellationToken, moduleName);
		List<string> filePaths = new List<string>(viewPrompts.Length)
		{
			frontReferencePath
		};
		for (int index = 1; index < viewPrompts.Length; index++)
		{
			long promptSeed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "three-view-cell", index.ToString());
			string prompt = BuildLocalThreeViewCellPrompt(node, entry, viewPrompts[index]);
			string viewNegativePrompt = MergeCommaPromptFragments(effectiveNegativePrompt, BuildLocalThreeViewViewNegativePrompt(index));
			List<string> list = filePaths;
			list.Add((await ExecuteComfyUiImageAsync(imageToImageModel, node, prompt, viewNegativePrompt, cancellationToken, 512, 1024, $"{node.Id}_{SanitizeFileSegment(entry.Name)}_threeview_{index + 1}", promptSeed, moduleName, BuildReferenceImageList(frontReferencePath))).Path);
		}
		string boardPath = ComposeGeneratedImageBoard(node, filePaths, 3, 1, node.Id + "_" + SanitizeFileSegment(entry.Name) + "_threeview_sheet");
		DeleteIntermediateImages(filePaths);
		return new GeneratedArtifact(boardPath, "已通过 ComfyUI 生成角色正面、侧面、背面三视图。");
	}

	private static async Task<GeneratedArtifact> GenerateStableDiffusionCharacterExpressionSheetAsync(ModelInfo textToImageModel, ModelInfo imageToImageModel, WorkflowNode node, CharacterDesignEntry entry, string basePrompt, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null)
	{
		IReadOnlyList<CharacterPromptTextBuilder.ExpressionCell> expressions = CharacterPromptTextBuilder.GetExpressions(entry);
		string expressionNegativePrompt = MergeCommaPromptFragments(WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry), negativePrompt, BuildExpressionIdentityNegativePrompt(entry));
		string referenceNegativePrompt = MergeCommaPromptFragments(WorkflowExecutor.BuildCharacterExpressionNegativePrompt(node, entry), negativePrompt);
		string referencePortraitPath = await EnsureStableDiffusionCharacterReferencePortraitAsync(textToImageModel, node, entry, basePrompt, referenceNegativePrompt, cancellationToken, moduleName, useSimpleReferencePrompt: true);
		List<string> filePaths = new List<string>(expressions.Count)
		{
			referencePortraitPath
		};
		for (int index = 1; index < expressions.Count; index++)
		{
			long promptSeed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "sd-expression-cell", index.ToString());
			string prompt = BuildLocalExpressionCellPrompt(node, entry, BuildLocalExpressionTargetPrompt(expressions[index].EnglishLabel));
			filePaths.Add((await ExecuteStableDiffusionImageToImageAsync(imageToImageModel, node, prompt, referencePortraitPath, cancellationToken, 512, 512, expressionNegativePrompt, moduleName, promptSeed, 0.70)).Path);
		}

		string boardPath = ComposeGeneratedImageBoard(node, filePaths, 3, 3, node.Id + "_" + SanitizeFileSegment(entry.Name) + "_expression_sheet", BuildExpressionBoardLabels(expressions));
		DeleteIntermediateImages(filePaths.Where(path => !string.Equals(path, referencePortraitPath, StringComparison.OrdinalIgnoreCase)).ToList());
		return new GeneratedArtifact(boardPath, "已通过本地 Stable Diffusion 生成角色九宫格表情板。");
	}

	private static async Task<GeneratedArtifact> GenerateStableDiffusionCharacterThreeViewSheetAsync(ModelInfo textToImageModel, ModelInfo imageToImageModel, WorkflowNode node, CharacterDesignEntry entry, string basePrompt, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null)
	{
		string[] viewPrompts = new string[3]
		{
			"STRICT FRONT FULL-BODY VIEW ONLY, exact straight-on orthographic front view, chest facing camera, hips facing camera, both eyes visible, both shoulders even, no side angle",
			"STRICT LEFT SIDE FULL-BODY VIEW ONLY, exact 90-degree profile, body facing left, head aligned with body facing left, " + CharacterThreeViewStrictSidePrompt,
			"STRICT BACK FULL-BODY VIEW ONLY, exact 180-degree back view, face fully hidden, no eyes visible, no nose visible, no mouth visible, back of head visible, back of outfit visible, back of shoes visible, both shoulders seen from behind"
		};
		string effectiveNegativePrompt = MergeCommaPromptFragments(negativePrompt, BuildThreeViewCellNegativePrompt(), BuildThreeViewIdentityNegativePrompt(entry));
		string faceReferencePath = ResolveCharacterFaceReferenceImagePath(node, entry);
		if (string.IsNullOrWhiteSpace(faceReferencePath))
		{
			faceReferencePath = await EnsureStableDiffusionCharacterReferencePortraitAsync(textToImageModel, node, entry, basePrompt, negativePrompt, cancellationToken, moduleName);
		}
		string frontReferencePath = await EnsureStableDiffusionCharacterFrontThreeViewReferenceAsync(imageToImageModel, node, entry, faceReferencePath, effectiveNegativePrompt, cancellationToken, moduleName);
		List<string> filePaths = new List<string>(viewPrompts.Length)
		{
			frontReferencePath
		};
		for (int index = 1; index < viewPrompts.Length; index++)
		{
			long promptSeed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "sd-three-view-cell", index.ToString());
			string prompt = BuildLocalThreeViewCellPrompt(node, entry, viewPrompts[index]);
			string viewNegativePrompt = MergeCommaPromptFragments(effectiveNegativePrompt, BuildLocalThreeViewViewNegativePrompt(index));
			double denoise = (index == 2) ? 0.90 : 0.92;
			filePaths.Add((await ExecuteStableDiffusionImageToImageAsync(imageToImageModel, node, prompt, frontReferencePath, cancellationToken, 512, 1024, viewNegativePrompt, moduleName, promptSeed, denoise)).Path);
		}

		string boardPath = ComposeGeneratedImageBoard(node, filePaths, 3, 1, node.Id + "_" + SanitizeFileSegment(entry.Name) + "_threeview_sheet");
		DeleteIntermediateImages(filePaths);
		return new GeneratedArtifact(boardPath, "已通过本地 Stable Diffusion 按参考脸生成角色正面、侧面、背面三视图。");
	}

	private static async Task<string> EnsureComfyUiCharacterFrontThreeViewReferenceAsync(ModelInfo model, WorkflowNode node, CharacterDesignEntry entry, string faceReferencePath, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null)
	{
		if (string.IsNullOrWhiteSpace(faceReferencePath))
		{
			throw new InvalidOperationException("缺少角色正面脸参考图，无法生成本地三视图正面全身参考。");
		}

		long seed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "comfy-threeview-front-reference");
		string prompt = BuildLocalThreeViewFrontReferencePrompt(node, entry);
		string seedCanvasPath = CreateThreeViewFrontSeedCanvas(node, entry, faceReferencePath);
		GeneratedArtifact generatedArtifact = await ExecuteComfyUiImageAsync(model, node, prompt, negativePrompt, cancellationToken, 512, 1024, $"{node.Id}_{SanitizeFileSegment(entry.Name)}_threeview_frontref", seed, moduleName, BuildReferenceImageList(seedCanvasPath));
		return generatedArtifact.Path;
	}

	private static async Task<string> EnsureStableDiffusionCharacterFrontThreeViewReferenceAsync(ModelInfo model, WorkflowNode node, CharacterDesignEntry entry, string faceReferencePath, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null)
	{
		if (string.IsNullOrWhiteSpace(faceReferencePath))
		{
			throw new InvalidOperationException("缺少角色正面脸参考图，无法生成本地三视图正面全身参考。");
		}

		long seed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "sd-threeview-front-reference");
		string prompt = BuildLocalThreeViewFrontReferencePrompt(node, entry);
		string seedCanvasPath = CreateThreeViewFrontSeedCanvas(node, entry, faceReferencePath);
		GeneratedArtifact generatedArtifact = await ExecuteStableDiffusionImageToImageAsync(model, node, prompt, seedCanvasPath, cancellationToken, 512, 1024, negativePrompt, moduleName, seed, 0.90);
		return generatedArtifact.Path;
	}

	private static async Task<string> EnsureStableDiffusionCharacterReferencePortraitAsync(ModelInfo model, WorkflowNode node, CharacterDesignEntry entry, string basePrompt, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null, bool useSimpleReferencePrompt = false)
	{
		if (entry.HasReferencePortrait && !useSimpleReferencePrompt)
		{
			return entry.ReferencePortraitPath;
		}

		string prompt = useSimpleReferencePrompt
			? BuildLocalFirstExpressionReferencePrompt(node, entry)
			: basePrompt.Trim().TrimEnd(',') + ", neutral calm expression, closed lips, no smile, neutral brows, exact straight-on front close-up head-and-shoulders portrait, face parallel to camera, head level, no head turn, no head tilt, both eyes equally visible, both cheeks symmetrically visible, face, neck, collar neckline and top of shoulders only, crop at the collarbone, shoulders-above framing, no chest, no bust, no torso, no waist, no belt, no pants, no arms, no hands, centered composition, " + CharacterExpressionCleanBackgroundPrompt + ", same character identity, no props, no extra person, " + WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node);
		long seed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "sd-reference-front");
		GeneratedArtifact generatedArtifact = await ExecuteStableDiffusionImageAsync(model, node, prompt, cancellationToken, 512, 512, negativePrompt, moduleName, seed);
		entry.ReferencePortraitPath = generatedArtifact.Path;
		return generatedArtifact.Path;
	}

	private static async Task<string> EnsureComfyUiCharacterReferencePortraitAsync(ModelInfo model, WorkflowNode node, CharacterDesignEntry entry, string basePrompt, string negativePrompt, CancellationToken cancellationToken, string? moduleName = null, bool useSimpleReferencePrompt = false)
	{
		if (entry.HasReferencePortrait && !useSimpleReferencePrompt)
		{
			return entry.ReferencePortraitPath;
		}

		string prompt = useSimpleReferencePrompt
			? BuildLocalFirstExpressionReferencePrompt(node, entry)
			: basePrompt.Trim().TrimEnd(',') + ", neutral calm expression, closed lips, no smile, neutral brows, exact straight-on front close-up head-and-shoulders portrait, face parallel to camera, head level, no head turn, no head tilt, both eyes equally visible, both cheeks symmetrically visible, face, neck, collar neckline and top of shoulders only, crop at the collarbone, shoulders-above framing, no chest, no bust, no torso, no waist, no belt, no pants, no arms, no hands, centered composition, " + CharacterExpressionCleanBackgroundPrompt + ", same character identity, no props, no extra person, " + WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node);
		long seed = BuildStableSeed(node.Id, entry.Name, entry.AppearancePrompt, entry.CostumeNotes, "reference-front");
		GeneratedArtifact generatedArtifact = await ExecuteComfyUiImageAsync(model, node, prompt, negativePrompt, cancellationToken, 512, 512, $"{node.Id}_{SanitizeFileSegment(entry.Name)}_reference_front", seed, moduleName);
		entry.ReferencePortraitPath = generatedArtifact.Path;
		return generatedArtifact.Path;
	}

	private static string BuildLocalFirstExpressionReferencePrompt(WorkflowNode node, CharacterDesignEntry entry)
	{
		string name = string.IsNullOrWhiteSpace(entry.Name) ? "the character" : entry.Name.Trim();
		string gender = CharacterPromptTextBuilder.DetectGender(entry) switch
		{
			CharacterGenderHint.Male => "adult male portrait, masculine face, no woman, no girl",
			CharacterGenderHint.Female => "adult female portrait, feminine face, no man, no boy, no beard",
			_ => "adult character portrait, preserve the described gender"
		};
		string facialHair = CharacterPromptTextBuilder.DetectGender(entry) == CharacterGenderHint.Male
			? "include described beard or mustache only if it is in the character setting"
			: string.Empty;
		var basicStats = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.BasicStats);
		var appearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
		var costumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);

		return MergeCommaPromptFragments(
			"single neutral front portrait of " + name,
			gender,
			LimitPromptFragment(basicStats, 120),
			LimitPromptFragment(appearancePrompt, 180),
			string.IsNullOrWhiteSpace(costumeNotes) ? string.Empty : "visible collar and shoulders: " + LimitPromptFragment(costumeNotes, 120),
			"one exact outfit anchor only, no alternate clothing, no outfit switching, keep the same visible collar and neckline",
			facialHair,
			"neutral calm expression, closed lips, relaxed brows",
			"straight-on front close-up head-and-shoulders portrait, face parallel to camera, centered face",
			CharacterSingleExpressionPortraitPrompt,
			CharacterExpressionBustFramingPrompt,
			"face, neck, collar neckline and top of shoulders clearly visible, crop at collarbone, no chest, no bust, no torso, no arms, no hands",
			CharacterExpressionCleanBackgroundPrompt,
			"soft even studio lighting",
			WorkflowExecutor.ResolveCharacterDesignStyleDescriptor(node),
			"one person only, one head only, one face only, no duplicate face, no hands, no props, no text, no watermark");
	}

	private static string LimitPromptFragment(string? value, int maxLength)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string text = Regex.Replace(value, "\\s+", " ").Trim().Trim(',', '，', '.', '。', ';', '；');
		if (text.Length <= maxLength)
		{
			return text;
		}

		int cut = text.LastIndexOfAny(new[] { ',', '，', ';', '；', '.', '。' }, Math.Min(text.Length - 1, maxLength));
		if (cut < maxLength / 2)
		{
			cut = maxLength;
		}

		return text[..cut].Trim().Trim(',', '，', '.', '。', ';', '；');
	}

	private static IReadOnlyList<string> BuildExpressionBoardLabels(IReadOnlyList<CharacterPromptTextBuilder.ExpressionCell> expressions)
	{
		return expressions
			.Select(expression => (expression.EnglishLabel ?? string.Empty).Trim().ToLowerInvariant() switch
			{
				"neutral expression" => "Neutral (calm)",
				"smile" => "Smile (gentle)",
				"angry" => "Angry (serious, frowning)",
				"surprised" => "Surprised (wide eyes)",
				"sad" => "Sad (forlorn)",
				"laughing" => "Laughing (happy)",
				"thinking expression" => "Thinking (puzzled)",
				"peaceful" => "Peaceful (eyes closed)",
				"skeptical expression" => "Skeptical (raised eyebrow)",
				_ => expression.EnglishLabel ?? string.Empty
			})
			.ToList();
	}

	private static IReadOnlyList<string> BuildStoryboardPanelSubtitleLabels(IReadOnlyList<StoryboardShot> shots)
	{
		return shots.Select(shot => NormalizeStoryboardSubtitleText(shot.Dialogue)).ToList();
	}

	private static string NormalizeStoryboardSubtitleText(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string text = value
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\r", "\n", StringComparison.Ordinal)
			.Trim();
		if (string.Equals(text, "无", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(text, "None", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(text, "N/A", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		text = Regex.Replace(text, @"\[[A-Za-z][A-Za-z\s,.;:'""!?-]{0,40}\]\s*", string.Empty);
		text = Regex.Replace(text, @"\b(None|N/A|null)\b", "无", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"\s+", " ").Trim();
		text = KeepSimplifiedChineseVisibleTextOnly(text);
		return text.Length > 56 ? text[..56].TrimEnd() + "…" : text;
	}

	private static string KeepSimplifiedChineseVisibleTextOnly(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		string filtered = Regex.Replace(text, @"\[[^\]]*\]", string.Empty);
		filtered = Regex.Replace(filtered, @"[A-Za-z0-9_./\\:@#%&+=*<>|~`^$]+", string.Empty);
		filtered = Regex.Replace(filtered, @"[^\u3400-\u4DBF\u4E00-\u9FFF，。！？、：；“”‘’（）《》【】—…\s]", string.Empty);
		filtered = Regex.Replace(filtered, @"\s+", " ").Trim();
		return Regex.IsMatch(filtered, @"[\u3400-\u4DBF\u4E00-\u9FFF]") ? filtered : string.Empty;
	}

	private static string KeepSimplifiedChineseVisibleTextBlock(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		return string.Join(
			Environment.NewLine,
			text.Replace("\r\n", "\n", StringComparison.Ordinal)
				.Replace("\r", "\n", StringComparison.Ordinal)
				.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(KeepSimplifiedChineseVisibleTextOnly)
				.Where(line => !string.IsNullOrWhiteSpace(line)));
	}

	private static string ComposeGeneratedImageBoard(
		WorkflowNode node,
		IReadOnlyList<string> imagePaths,
		int columns,
		int rows,
		string filePrefix,
		IReadOnlyList<string>? labels = null,
		IReadOnlyList<string>? subtitles = null)
	{
		if (imagePaths.Count == 0)
		{
			throw new InvalidOperationException("没有可用于拼板的图片。");
		}
		using DisposableBitmapCollection disposableBitmapCollection = new DisposableBitmapCollection(imagePaths);
		bool isStoryboardBoard = node.Type == WorkflowNodeCatalog.StoryboardImage &&
			filePrefix.Contains("_storyboard_page_", StringComparison.OrdinalIgnoreCase);
		var storyboardCellSize = isStoryboardBoard
			? GetStoryboardBoardCellSize(node)
			: (Width: 0, Height: 0);
		int cellWidth = isStoryboardBoard
			? storyboardCellSize.Width
			: disposableBitmapCollection.Items.Max((Bitmap bitmap3) => bitmap3.Width);
		int cellHeight = isStoryboardBoard
			? storyboardCellSize.Height
			: disposableBitmapCollection.Items.Max((Bitmap bitmap3) => bitmap3.Height);
		int width = columns * cellWidth + (columns - 1) * 24 + 56;
		int height = rows * cellHeight + (rows - 1) * 24 + 56;
		using Bitmap bitmap = new Bitmap(width, height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.Clear(Color.FromArgb(18, 20, 26));
		graphics.SmoothingMode = SmoothingMode.HighQuality;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		using Pen pen = new Pen(Color.FromArgb(52, 57, 70), 2f);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(24, 27, 34));
		using SolidBrush labelBackBrush = new SolidBrush(Color.FromArgb(185, 0, 0, 0));
		using SolidBrush labelTextBrush = new SolidBrush(Color.White);
		using Font labelFont = CreateChineseOverlayFont(Math.Clamp(cellWidth / 24f, 10f, 16f), FontStyle.Bold);
		using StringFormat labelFormat = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			Trimming = StringTrimming.EllipsisCharacter,
			FormatFlags = StringFormatFlags.NoWrap
		};
		bool cropExpressionCells = filePrefix.Contains("_expression_sheet", StringComparison.OrdinalIgnoreCase);
		for (int num3 = 0; num3 < rows; num3++)
		{
			for (int num4 = 0; num4 < columns; num4++)
			{
				int x = 28 + num4 * (cellWidth + 24);
				int y = 28 + num3 * (cellHeight + 24);
				graphics.FillRectangle(brush, x, y, cellWidth, cellHeight);
				graphics.DrawRectangle(pen, x, y, cellWidth - 1, cellHeight - 1);
			}
		}
		for (int num5 = 0; num5 < disposableBitmapCollection.Items.Count; num5++)
		{
			Bitmap bitmap2 = disposableBitmapCollection.Items[num5];
			int num6 = num5 / columns;
			int num7 = num5 % columns;
			if (num6 >= rows)
			{
				break;
			}
			int num8 = 28 + num7 * (cellWidth + 24);
			int num9 = 28 + num6 * (cellHeight + 24);
			var cellBounds = new Rectangle(num8, num9, cellWidth, cellHeight);
			if (cropExpressionCells)
			{
				Rectangle sourceBounds = ComputeExpressionBustSourceBounds(bitmap2.Width, bitmap2.Height, cellWidth, cellHeight);
				graphics.DrawImage(bitmap2, cellBounds, sourceBounds, GraphicsUnit.Pixel);
			}
			else if (isStoryboardBoard)
			{
				Rectangle sourceBounds = ComputeImageFillSourceBounds(bitmap2.Width, bitmap2.Height, cellWidth, cellHeight);
				graphics.DrawImage(bitmap2, cellBounds, sourceBounds, GraphicsUnit.Pixel);
			}
			else
			{
				Rectangle imageBounds = ComputeImageFitBounds(num8, num9, cellWidth, cellHeight, bitmap2.Width, bitmap2.Height);
				graphics.DrawImage(bitmap2, imageBounds);
			}
			if (isStoryboardBoard && subtitles != null && num5 < subtitles.Count && !string.IsNullOrWhiteSpace(subtitles[num5]))
			{
				DrawStoryboardSubtitle(graphics, cellBounds, subtitles[num5]);
			}
			if (labels != null && num5 < labels.Count && !string.IsNullOrWhiteSpace(labels[num5]))
			{
				int labelHeight = Math.Clamp(cellHeight / 10, 24, 44);
				Rectangle labelBounds = new Rectangle(num8, num9 + cellHeight - labelHeight, cellWidth, labelHeight);
				graphics.FillRectangle(labelBackBrush, labelBounds);
				graphics.DrawString(labels[num5], labelFont, labelTextBrush, labelBounds, labelFormat);
			}
		}
		string path = EnsureOutputDirectory(node.Type);
		string text = Path.Combine(path, $"{SanitizeFileSegment(filePrefix)}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
		bitmap.Save(text, ImageFormat.Png);
		return text;
	}

	private static (int Width, int Height) GetStoryboardBoardCellSize(WorkflowNode node)
	{
		node.Params ??= new WorkflowNodeParameters();
		node.Params.EnsureDefaults(node.Type);
		return string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal)
			? (Width: 1080, Height: 1920)
			: (Width: 1920, Height: 1080);
	}

	private static Font CreateChineseOverlayFont(float size, FontStyle style)
	{
		try
		{
			return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel);
		}
		catch
		{
			return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel);
		}
	}

	private static void DrawStoryboardSubtitle(Graphics graphics, Rectangle cellBounds, string subtitle)
	{
		int subtitleHeight = Math.Clamp(cellBounds.Height / 7, 38, 72);
		var subtitleBounds = new Rectangle(
			cellBounds.X,
			cellBounds.Bottom - subtitleHeight,
			cellBounds.Width,
			subtitleHeight);
		using SolidBrush backBrush = new SolidBrush(Color.FromArgb(178, 0, 0, 0));
		using SolidBrush textBrush = new SolidBrush(Color.White);
		using Font subtitleFont = CreateChineseOverlayFont(Math.Clamp(cellBounds.Width / 32f, 16f, 26f), FontStyle.Bold);
		using StringFormat subtitleFormat = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			Trimming = StringTrimming.EllipsisWord,
		};

		graphics.FillRectangle(backBrush, subtitleBounds);
		graphics.DrawString(subtitle, subtitleFont, textBrush, subtitleBounds, subtitleFormat);
	}

	private static Rectangle ComputeImageFitBounds(int cellX, int cellY, int cellWidth, int cellHeight, int imageWidth, int imageHeight)
	{
		if (cellWidth <= 0 || cellHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
		{
			return new Rectangle(cellX, cellY, Math.Max(1, cellWidth), Math.Max(1, cellHeight));
		}

		double scale = Math.Min(cellWidth / (double)imageWidth, cellHeight / (double)imageHeight);
		int drawWidth = Math.Max(1, (int)Math.Round(imageWidth * scale));
		int drawHeight = Math.Max(1, (int)Math.Round(imageHeight * scale));
		return new Rectangle(
			cellX + (cellWidth - drawWidth) / 2,
			cellY + (cellHeight - drawHeight) / 2,
			drawWidth,
			drawHeight);
	}

	private static Rectangle ComputeImageFillSourceBounds(int imageWidth, int imageHeight, int cellWidth, int cellHeight)
	{
		if (cellWidth <= 0 || cellHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
		{
			return new Rectangle(0, 0, Math.Max(1, imageWidth), Math.Max(1, imageHeight));
		}

		double targetAspect = cellWidth / (double)cellHeight;
		double imageAspect = imageWidth / (double)imageHeight;
		if (imageAspect > targetAspect)
		{
			int sourceWidth = Math.Max(1, (int)Math.Round(imageHeight * targetAspect));
			int sourceX = Math.Max(0, (imageWidth - sourceWidth) / 2);
			return new Rectangle(sourceX, 0, Math.Min(sourceWidth, imageWidth - sourceX), imageHeight);
		}

		int sourceHeight = Math.Max(1, (int)Math.Round(imageWidth / targetAspect));
		int sourceY = Math.Max(0, (imageHeight - sourceHeight) / 2);
		return new Rectangle(0, sourceY, imageWidth, Math.Min(sourceHeight, imageHeight - sourceY));
	}

	private static Rectangle ComputeExpressionBustSourceBounds(int imageWidth, int imageHeight, int cellWidth, int cellHeight)
	{
		if (imageWidth <= 0 || imageHeight <= 0 || cellWidth <= 0 || cellHeight <= 0)
		{
			return new Rectangle(0, 0, Math.Max(1, imageWidth), Math.Max(1, imageHeight));
		}

		double targetAspect = cellWidth / (double)cellHeight;
		int cropHeight = Math.Clamp((int)Math.Round(imageHeight * 0.72), 1, imageHeight);
		int cropWidth = Math.Clamp((int)Math.Round(cropHeight * targetAspect), 1, imageWidth);
		if (cropWidth == imageWidth)
		{
			cropHeight = Math.Clamp((int)Math.Round(cropWidth / targetAspect), 1, imageHeight);
		}

		int x = Math.Max(0, (imageWidth - cropWidth) / 2);
		return new Rectangle(x, 0, cropWidth, cropHeight);
	}

	private static void DeleteIntermediateImages(IEnumerable<string> imagePaths)
	{
		foreach (string item in imagePaths.Where((string path) => !string.IsNullOrWhiteSpace(path)))
		{
			try
			{
				if (File.Exists(item))
				{
					File.Delete(item);
				}
			}
			catch
			{
			}
		}
	}

	private static JsonObject LoadComfyUiWorkflowTemplate(bool preferReferenceTemplate = false)
	{
		return LoadComfyUiWorkflowTemplate(null, preferReferenceTemplate);
	}

	private static string? ResolveConfiguredComfyUiWorkflowTemplateFileName(ModelInfo model, bool preferReferenceTemplate)
	{
		string configuredWorkflowJson = ModelConfig.ResolveComfyUiWorkflowJson(model);
		if (!string.IsNullOrWhiteSpace(configuredWorkflowJson))
		{
			return configuredWorkflowJson;
		}

		return preferReferenceTemplate ? ComfyUiReferenceTemplateFileName : ComfyUiTemplateFileName;
	}

	private static JsonObject LoadComfyUiWorkflowTemplate(string? templateFileName, bool preferReferenceTemplate = false)
	{
		string text = FindComfyUiTemplatePath(templateFileName, preferReferenceTemplate);
		string input = File.ReadAllText(text, Encoding.UTF8);
		string json = Regex.Replace(input, ",?\\s*\"_meta\"\\s*:\\s*\\{.*?\\}", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
		if (!(JsonNode.Parse(json) is JsonObject result))
		{
			throw new InvalidOperationException("无法读取 ComfyUI workflow 模板：" + text);
		}
		return result;
	}

	private static string FindComfyUiTemplatePath(bool preferReferenceTemplate = false)
	{
		return FindComfyUiTemplatePath(null, preferReferenceTemplate);
	}

	private static string FindComfyUiTemplatePath(string? templateFileName, bool preferReferenceTemplate = false)
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		List<string> list = new List<string>();
		string[] array = new string[3]
		{
			Path.Combine(baseDirectory, "Workflowsapi"),
			Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "WinApp", "bin", "Debug", "net8.0-windows", "Workflowsapi")),
			Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Workflowsapi"))
		};
		string[] array2 = string.IsNullOrWhiteSpace(templateFileName)
			? (preferReferenceTemplate ? new string[1] { ComfyUiReferenceTemplateFileName } : new string[2] { ComfyUiTemplateFileName, ComfyUiReferenceTemplateFileName })
			: new string[1] { templateFileName };
		foreach (string text in array.Where((string path) => !string.IsNullOrWhiteSpace(path)))
		{
			foreach (string text2 in array2)
			{
				list.Add(Path.Combine(text, text2));
			}
		}

		foreach (string text3 in array2)
		{
			list.Add(Path.Combine(baseDirectory, text3));
			list.Add(Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", text3)));
			list.Add(Path.Combine(Environment.CurrentDirectory, text3));
		}
		foreach (string item in list.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (File.Exists(item))
			{
				return item;
			}
		}

		if (preferReferenceTemplate && string.IsNullOrWhiteSpace(templateFileName))
		{
			throw new FileNotFoundException("未找到 ComfyUI 图生图工作流模板 FaceimgtoimgWorkflow.json。请检查客户端输出目录中的 Workflowsapi 文件夹是否完整。");
		}

		throw new FileNotFoundException("未找到 ComfyUI workflow 模板文件。");
	}

	private static void ConfigureComfyUiWorkflow(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long? seed, string? uploadedReferenceImageName = null, string? workflowTemplateFileName = null)
	{
		long effectiveSeed = seed ?? Random.Shared.NextInt64(1L, long.MaxValue);
		HashSet<string> appliedPlaceholders = ApplyComfyUiWorkflowPlaceholders(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, effectiveSeed, uploadedReferenceImageName);
		bool hasPlaceholders = appliedPlaceholders.Count > 0;
		ConfigureComfyUiWorkflowByKnownNodes(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, effectiveSeed, uploadedReferenceImageName, workflowTemplateFileName, hasPlaceholders, appliedPlaceholders);
	}

	private static void ConfigureComfyUiWorkflowByKnownNodes(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName, string? workflowTemplateFileName, bool hasPlaceholders, HashSet<string> appliedPlaceholders)
	{
		JsonObject? samplerNode = FindWorkflowNodeByClassType(workflow, "KSampler");
		JsonObject? samplerInputs = samplerNode?["inputs"] as JsonObject;
		if (samplerInputs == null)
		{
			if (hasPlaceholders)
			{
				return;
			}

			throw new InvalidOperationException("ComfyUI workflow 中缺少 KSampler 节点。请在工作流 JSON 中加入 {{positive_prompt}}、{{negative_prompt}}、{{seed}}、{{width}}、{{height}} 等占位符，或使用标准 ComfyUI KSampler 工作流。");
		}

		JsonObject? positiveNode = ResolveConnectedWorkflowNode(workflow, samplerInputs, "positive") ?? FindWorkflowNodeByClassType(workflow, "CLIPTextEncode");
		JsonObject? negativeNode = ResolveConnectedWorkflowNode(workflow, samplerInputs, "negative") ?? FindWorkflowNodeByClassType(workflow, "CLIPTextEncode", 1) ?? positiveNode;
		JsonObject? latentNode = ResolveConnectedWorkflowNode(workflow, samplerInputs, "latent_image") ?? FindWorkflowNodeByClassType(workflow, "EmptyLatentImage") ?? FindWorkflowNodeByClassType(workflow, "VAEEncode");
		JsonObject? modelNode = ResolveConnectedWorkflowNode(workflow, samplerInputs, "model") ?? FindWorkflowNodeByClassType(workflow, "CheckpointLoaderSimple");
		JsonObject? saveImageNode = FindWorkflowNodeByClassType(workflow, "SaveImage");
		JsonObject? loadImageNode = FindWorkflowNodeByClassType(workflow, "LoadImage");

		if (TryConfigureFlux2KleinWorkflow(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName, workflowTemplateFileName))
		{
			return;
		}

		if (positiveNode?["inputs"] is JsonObject positiveInputs && !appliedPlaceholders.Contains("positive_prompt"))
		{
			positiveInputs["text"] = JsonValue.Create(prompt);
		}
		else if (!hasPlaceholders)
		{
			throw new InvalidOperationException("ComfyUI workflow 中缺少正向提示词节点。");
		}

		if (negativeNode?["inputs"] is JsonObject negativeInputs && !appliedPlaceholders.Contains("negative_prompt"))
		{
			string originalNegativePrompt = negativeInputs["text"]?.GetValue<string>() ?? string.Empty;
			negativeInputs["text"] = JsonValue.Create(BuildComfyUiNegativePrompt(originalNegativePrompt, negativePrompt));
		}

		if (latentNode?["inputs"] is JsonObject latentInputs)
		{
			if (latentInputs.ContainsKey("width") && !appliedPlaceholders.Contains("width"))
			{
				latentInputs["width"] = JsonValue.Create(width);
			}
			if (latentInputs.ContainsKey("height") && !appliedPlaceholders.Contains("height"))
			{
				latentInputs["height"] = JsonValue.Create(height);
			}
			if (latentInputs.ContainsKey("batch_size") && !appliedPlaceholders.Contains("batch_size"))
			{
				latentInputs["batch_size"] = JsonValue.Create(1);
			}
		}
		else if (!hasPlaceholders)
		{
			throw new InvalidOperationException("ComfyUI workflow 中缺少 latent_image 节点。");
		}

		if (!appliedPlaceholders.Contains("seed"))
		{
			samplerInputs["seed"] = JsonValue.Create(seed);
		}
		if (!appliedPlaceholders.Contains("steps"))
		{
			samplerInputs["steps"] = JsonValue.Create((node.Type == "分镜图片") ? 26 : 24);
		}
		if (!appliedPlaceholders.Contains("cfg"))
		{
			samplerInputs["cfg"] = JsonValue.Create((node.Type == "分镜图片") ? 7.5 : 7.0);
		}
		if (!string.IsNullOrWhiteSpace(uploadedReferenceImageName) && samplerInputs.ContainsKey("denoise") && !appliedPlaceholders.Contains("denoise"))
		{
			samplerInputs["denoise"] = JsonValue.Create(ResolveReferenceDenoise(filePrefix));
		}

		if (modelNode?["inputs"] is JsonObject modelInputs && ShouldApplyConfiguredComfyUiCheckpoint(model) && !appliedPlaceholders.Contains("model_id"))
		{
			if (modelInputs.ContainsKey("ckpt_name"))
			{
				modelInputs["ckpt_name"] = JsonValue.Create(model.Id);
			}
			else if (modelInputs.ContainsKey("unet_name"))
			{
				modelInputs["unet_name"] = JsonValue.Create(model.Id);
			}
		}

		if (saveImageNode?["inputs"] is JsonObject saveImageInputs && !appliedPlaceholders.Contains("filename_prefix"))
		{
			saveImageInputs["filename_prefix"] = JsonValue.Create(BuildComfyUiFilePrefix(node, filePrefix));
		}

		if (!string.IsNullOrWhiteSpace(uploadedReferenceImageName) && !appliedPlaceholders.Contains("input_image"))
		{
			string workflowLabel = string.IsNullOrWhiteSpace(workflowTemplateFileName) ? "当前工作流" : workflowTemplateFileName;
			if (loadImageNode?["inputs"] is not JsonObject loadImageInputs)
			{
				throw new InvalidOperationException($"{workflowLabel} 缺少参考图入口，无法用于九宫格或三视图这类图生图任务。请在工作流 JSON 中使用 {{input_image}} 占位符，或提供标准 LoadImage 节点。");
			}

			string latentClassType = latentNode?["class_type"]?.GetValue<string>() ?? string.Empty;
			if (!hasPlaceholders && !latentClassType.Contains("VAEEncode", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"{workflowLabel} 没有将参考图编码进 latent 流程，无法用于九宫格或三视图这类参考图任务。请改用支持图生图的 workflow JSON。");
			}

			loadImageInputs["image"] = JsonValue.Create(uploadedReferenceImageName);
		}
	}

	private static bool TryConfigureFlux2KleinWorkflow(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName, string? workflowTemplateFileName)
	{
		var workflowName = workflowTemplateFileName ?? ModelConfig.ResolveComfyUiWorkflowJson(model);
		var looksFlux2Klein = ContainsWorkflowText(workflowName, "Flux2-Klein") ||
			ContainsWorkflowText(workflowName, "Flux.2-Klein") ||
			FindWorkflowNodeByClassType(workflow, "EmptyFlux2LatentImage") != null ||
			FindWorkflowNodeByClassType(workflow, "UnetLoaderGGUF") != null;
		if (!looksFlux2Klein)
		{
			return false;
		}

		var hasReferenceImage = !string.IsNullOrWhiteSpace(uploadedReferenceImageName);
		var looksImageToImage = ContainsWorkflowText(workflowName, "图生图") ||
			ContainsWorkflowText(workflowName, "img2img") ||
			ContainsWorkflowText(workflowName, "edit") ||
			FindWorkflowNodeByClassType(workflow, "ReferenceLatent") != null ||
			workflow["76"] is JsonObject;
		var looksTextToImage = ContainsWorkflowText(workflowName, "文生图") ||
			workflow["93"] is JsonObject;

		if (looksImageToImage)
		{
			if (!hasReferenceImage)
			{
				throw new InvalidOperationException($"{workflowName} 是 Flux2-Klein 图生图工作流，需要参考图；请把它配置到角色节点的“图生图模型”槽位。");
			}

			ConfigureFlux2KleinImageToImageWorkflow(workflow, model, node, prompt, negativePrompt, filePrefix, seed, uploadedReferenceImageName!);
			return true;
		}

		if (looksTextToImage)
		{
			if (hasReferenceImage)
			{
				throw new InvalidOperationException($"{workflowName} 是 Flux2-Klein 文生图工作流，不包含参考图入口；请把图生图任务改用 Flux2-Klein-图生图 工作流。");
			}

			ConfigureFlux2KleinTextToImageWorkflow(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, seed);
			return true;
		}

		return false;
	}

	private static void ConfigureFlux2KleinTextToImageWorkflow(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed)
	{
		SetWorkflowTextInput(workflow, "93", prompt);
		SetWorkflowTextInput(workflow, "86", MergeWorkflowNegativePrompt(workflow, "86", negativePrompt));
		SetWorkflowInput(workflow, "85", "width", width);
		SetWorkflowInput(workflow, "85", "height", height);
		SetWorkflowInput(workflow, "85", "batch_size", 1);
		SetWorkflowInput(workflow, "98", "seed", seed);
		SetWorkflowInput(workflow, "105", "filename_prefix", BuildComfyUiFilePrefix(node, filePrefix));
		ApplyFlux2KleinUnetModel(workflow, model, "111");
	}

	private static void ConfigureFlux2KleinImageToImageWorkflow(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, string? filePrefix, long seed, string uploadedReferenceImageName)
	{
		SetWorkflowInput(workflow, "76", "image", uploadedReferenceImageName);
		SetWorkflowTextInput(workflow, "108", prompt);
		SetWorkflowTextInput(workflow, "109", MergeWorkflowNegativePrompt(workflow, "109", negativePrompt));
		SetWorkflowInput(workflow, "146", "seed", seed);
		SetWorkflowInput(workflow, "9", "filename_prefix", BuildComfyUiFilePrefix(node, filePrefix));
		ApplyFlux2KleinUnetModel(workflow, model, "163");
	}

	private static void ApplyFlux2KleinUnetModel(JsonObject workflow, ModelInfo model, string nodeId)
	{
		if (!ShouldApplyConfiguredComfyUiCheckpoint(model))
		{
			return;
		}

		if (TryGetWorkflowInputs(workflow, nodeId, out var inputs) && inputs.ContainsKey("unet_name"))
		{
			inputs["unet_name"] = JsonValue.Create(model.Id);
		}
	}

	private static bool ContainsWorkflowText(string? text, string value)
	{
		return !string.IsNullOrWhiteSpace(text) &&
			text.Contains(value, StringComparison.OrdinalIgnoreCase);
	}

	private static string MergeWorkflowNegativePrompt(JsonObject workflow, string nodeId, string negativePrompt)
	{
		var original = string.Empty;
		if (TryGetWorkflowInputs(workflow, nodeId, out var inputs) &&
			inputs["text"] is JsonValue textValue &&
			textValue.TryGetValue<string>(out var text))
		{
			original = text;
		}

		return BuildComfyUiNegativePrompt(original, negativePrompt);
	}

	private static void SetWorkflowTextInput(JsonObject workflow, string nodeId, string value)
	{
		SetWorkflowInput(workflow, nodeId, "text", value ?? string.Empty);
	}

	private static void SetWorkflowInput(JsonObject workflow, string nodeId, string inputName, string value)
	{
		if (TryGetWorkflowInputs(workflow, nodeId, out var inputs))
		{
			inputs[inputName] = JsonValue.Create(value ?? string.Empty);
		}
	}

	private static void SetWorkflowInput(JsonObject workflow, string nodeId, string inputName, int value)
	{
		if (TryGetWorkflowInputs(workflow, nodeId, out var inputs))
		{
			inputs[inputName] = JsonValue.Create(value);
		}
	}

	private static void SetWorkflowInput(JsonObject workflow, string nodeId, string inputName, long value)
	{
		if (TryGetWorkflowInputs(workflow, nodeId, out var inputs))
		{
			inputs[inputName] = JsonValue.Create(value);
		}
	}

	private static bool TryGetWorkflowInputs(JsonObject workflow, string nodeId, out JsonObject inputs)
	{
		inputs = null!;
		if (workflow[nodeId] is not JsonObject nodeObject ||
			nodeObject["inputs"] is not JsonObject inputObject)
		{
			return false;
		}

		inputs = inputObject;
		return true;
	}

	private static HashSet<string> ApplyComfyUiWorkflowPlaceholders(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName)
	{
		HashSet<string> appliedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ApplyComfyUiWorkflowPlaceholdersToNode(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName, appliedKeys);
		return appliedKeys;
	}

	private static JsonNode? ApplyComfyUiWorkflowPlaceholdersToNode(JsonNode? node, ModelInfo model, WorkflowNode workflowNode, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName, HashSet<string> appliedKeys)
	{
		if (node is JsonObject jsonObject)
		{
			foreach (string key in jsonObject.Select(item => item.Key).ToList())
			{
				JsonNode? current = jsonObject[key];
				JsonNode? updated = ApplyComfyUiWorkflowPlaceholdersToNode(current, model, workflowNode, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName, appliedKeys);
				if (!ReferenceEquals(current, updated))
				{
					jsonObject[key] = updated;
				}
			}

			return jsonObject;
		}

		if (node is JsonArray jsonArray)
		{
			for (int index = 0; index < jsonArray.Count; index++)
			{
				JsonNode? current = jsonArray[index];
				JsonNode? updated = ApplyComfyUiWorkflowPlaceholdersToNode(current, model, workflowNode, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName, appliedKeys);
				if (!ReferenceEquals(current, updated))
				{
					jsonArray[index] = updated;
				}
			}

			return jsonArray;
		}

		if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text))
		{
			return ReplaceComfyUiWorkflowPlaceholderValue(text, model, workflowNode, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName, appliedKeys);
		}

		return node;
	}

	private static JsonNode? ReplaceComfyUiWorkflowPlaceholderValue(string text, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName, HashSet<string> appliedKeys)
	{
		MatchCollection matches = ComfyUiWorkflowPlaceholderRegex.Matches(text);
		if (matches.Count == 0)
		{
			return JsonValue.Create(text);
		}

		if (matches.Count == 1 && string.Equals(matches[0].Value, text, StringComparison.Ordinal))
		{
			string canonicalKey = NormalizeComfyUiWorkflowParameterName(matches[0].Groups[1].Value);
			appliedKeys.Add(canonicalKey);
			return CreateComfyUiWorkflowParameterJsonValue(canonicalKey, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName);
		}

		string replaced = ComfyUiWorkflowPlaceholderRegex.Replace(text, match =>
		{
			string canonicalKey = NormalizeComfyUiWorkflowParameterName(match.Groups[1].Value);
			appliedKeys.Add(canonicalKey);
			return GetComfyUiWorkflowParameterString(canonicalKey, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName);
		});
		return JsonValue.Create(replaced);
	}

	private static JsonNode? CreateComfyUiWorkflowParameterJsonValue(string canonicalKey, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName)
	{
		return canonicalKey switch
		{
			"seed" => JsonValue.Create(seed),
			"width" => JsonValue.Create(width),
			"height" => JsonValue.Create(height),
			"batch_size" => JsonValue.Create(1),
			"steps" => JsonValue.Create((node.Type == "分镜图片") ? 26 : 24),
			"cfg" => JsonValue.Create((node.Type == "分镜图片") ? 7.5 : 7.0),
			"denoise" => JsonValue.Create(string.IsNullOrWhiteSpace(uploadedReferenceImageName) ? 1.0 : ResolveReferenceDenoise(filePrefix)),
			_ => JsonValue.Create(GetComfyUiWorkflowParameterString(canonicalKey, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName))
		};
	}

	private static string GetComfyUiWorkflowParameterString(string canonicalKey, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string? filePrefix, long seed, string? uploadedReferenceImageName)
	{
		return canonicalKey switch
		{
			"positive_prompt" => prompt ?? string.Empty,
			"negative_prompt" => negativePrompt ?? string.Empty,
			"seed" => seed.ToString(CultureInfo.InvariantCulture),
			"width" => width.ToString(CultureInfo.InvariantCulture),
			"height" => height.ToString(CultureInfo.InvariantCulture),
			"batch_size" => "1",
			"steps" => ((node.Type == "分镜图片") ? 26 : 24).ToString(CultureInfo.InvariantCulture),
			"cfg" => ((node.Type == "分镜图片") ? 7.5 : 7.0).ToString(CultureInfo.InvariantCulture),
			"denoise" => (string.IsNullOrWhiteSpace(uploadedReferenceImageName) ? 1.0 : ResolveReferenceDenoise(filePrefix)).ToString(CultureInfo.InvariantCulture),
			"input_image" => uploadedReferenceImageName ?? string.Empty,
			"filename_prefix" => BuildComfyUiFilePrefix(node, filePrefix),
			"model_id" => model.Id ?? string.Empty,
			"model_name" => model.Name ?? string.Empty,
			"workflow_json" => ModelConfig.ResolveComfyUiWorkflowJson(model),
			_ => string.Empty
		};
	}

	private static string NormalizeComfyUiWorkflowParameterName(string rawName)
	{
		string key = Regex.Replace(rawName ?? string.Empty, "[\\s\\-.]+", "_").Trim('_').ToLowerInvariant();
		return key switch
		{
			"prompt" or "positive" or "positive_text" or "text_prompt" => "positive_prompt",
			"negative" or "negative_text" => "negative_prompt",
			"image" or "input_image_name" or "reference_image" or "reference_image_name" or "ref_image" or "uploaded_image" or "uploaded_reference_image" => "input_image",
			"file_prefix" or "prefix" or "output_prefix" or "save_prefix" => "filename_prefix",
			"checkpoint" or "ckpt" or "ckpt_name" or "model" => "model_id",
			"denoising_strength" or "strength" => "denoise",
			"batch" or "batchsize" => "batch_size",
			_ => key
		};
	}

	private static double ResolveReferenceDenoise(string? filePrefix)
	{
		if (filePrefix?.Contains("_threeview_frontref", StringComparison.OrdinalIgnoreCase) == true)
		{
			return 0.90;
		}

		if (filePrefix?.Contains("_threeview_3", StringComparison.OrdinalIgnoreCase) == true)
		{
			return 0.90;
		}

		if (filePrefix?.Contains("_threeview_2", StringComparison.OrdinalIgnoreCase) == true)
		{
			return 0.92;
		}

		if (filePrefix?.Contains("_threeview_", StringComparison.OrdinalIgnoreCase) == true)
		{
			return 0.82;
		}

		if (filePrefix?.Contains("_expression_", StringComparison.OrdinalIgnoreCase) == true)
		{
			return 0.70;
		}

		return 0.62;
	}

	private static async Task<string?> UploadComfyUiReferenceImageAsync(string baseUrl, IReadOnlyList<string>? referenceImagePaths, CancellationToken cancellationToken)
	{
		string text = referenceImagePaths?.FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		Uri endpoint = new Uri(new Uri(AppendTrailingSlash(baseUrl)), "upload/image");
		string text2 = "JSAI_ref_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + Path.GetExtension(text);
		await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
		{
			using MultipartFormDataContent multipartFormDataContent = new MultipartFormDataContent();
			byte[] array3 = await File.ReadAllBytesAsync(text, token);
			ByteArrayContent byteArrayContent = new ByteArrayContent(array3);
			byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue(ReadInlineImageData(text).MimeType);
			multipartFormDataContent.Add(byteArrayContent, "image", text2);
			multipartFormDataContent.Add(new StringContent("true"), "overwrite");
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
			{
				Content = multipartFormDataContent
			};
			PrepareComfyUiRequest(request);
			using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
			string responseText = await response.Content.ReadAsStringAsync(token);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"ComfyUI 上传参考图失败：{response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
			}
			if (!string.IsNullOrWhiteSpace(responseText))
			{
				try
				{
					using JsonDocument jsonDocument = JsonDocument.Parse(responseText);
					if (TryFindString(jsonDocument.RootElement, new string[2] { "name", "filename" }, out var value))
					{
						text2 = value;
					}
				}
				catch
				{
				}
			}
			return true;
		}, "上传参考图", cancellationToken);
		return text2;
	}

	private static JsonObject? FindWorkflowNodeByClassType(JsonObject workflow, string classType, int skip = 0)
	{
		int num = 0;
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (item.Value is JsonObject jsonObject)
			{
				string a = jsonObject["class_type"]?.GetValue<string>();
				if (string.Equals(a, classType, StringComparison.OrdinalIgnoreCase) && num++ >= skip)
				{
					return jsonObject;
				}
			}
		}
		return null;
	}

	private static JsonObject? ResolveConnectedWorkflowNode(JsonObject workflow, JsonObject sourceInputs, string inputName)
	{
		if (!(sourceInputs[inputName] is JsonArray { Count: not 0 } jsonArray))
		{
			return null;
		}
		string text = jsonArray[0]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return workflow[text] as JsonObject;
	}

	private static string BuildComfyUiNegativePrompt(string originalNegativePrompt, string requestedNegativePrompt)
	{
		string[] source = new string[3] { originalNegativePrompt, requestedNegativePrompt, "text, watermark, lowres, bad anatomy" };
		return string.Join(", ", from value in source
			where !string.IsNullOrWhiteSpace(value)
			select value.Trim().Trim(','));
	}

	private static bool ShouldOverrideComfyUiCheckpoint(string modelId)
	{
		if (string.IsNullOrWhiteSpace(modelId))
		{
			return false;
		}
		return modelId.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) ||
			modelId.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase) ||
			modelId.EndsWith(".pt", StringComparison.OrdinalIgnoreCase) ||
			modelId.EndsWith(".pth", StringComparison.OrdinalIgnoreCase) ||
			modelId.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);
	}

	private static bool ShouldApplyConfiguredComfyUiCheckpoint(ModelInfo model)
	{
		if (model == null || string.IsNullOrWhiteSpace(model.Id))
		{
			return false;
		}

		return ShouldOverrideComfyUiCheckpoint(model.Id);
	}

	private static string BuildComfyUiFilePrefix(WorkflowNode node, string? filePrefix)
	{
		string value = (string.IsNullOrWhiteSpace(filePrefix) ? (node.Type + "_" + node.Id) : filePrefix);
		return "JSAI_" + SanitizeFileSegment(value);
	}

	private static long BuildStableSeed(params string?[] values)
	{
		ulong num = 1469598103934665603uL;
		foreach (string text in values)
		{
			string text2 = text ?? string.Empty;
			string text3 = text2;
			string text4 = text3;
			foreach (char c in text4)
			{
				num ^= c;
				num *= 1099511628211L;
			}
			num ^= 0x1F;
			num *= 1099511628211L;
		}
		long num2 = (long)(num & 0x7FFFFFFFFFFFFFFFL);
		return (num2 == 0L) ? 1 : num2;
	}

	private static async Task<string> SubmitComfyUiPromptAsync(string baseUrl, JsonObject workflow, CancellationToken cancellationToken)
	{
		await NormalizeComfyUiWorkflowModelNamesAsync(baseUrl, workflow, cancellationToken);
		Uri endpoint = new Uri(new Uri(AppendTrailingSlash(baseUrl)), "prompt");
		JsonObject requestBody = new JsonObject
		{
			["client_id"] = $"jsai-{Guid.NewGuid():N}",
			["prompt"] = workflow
		};
		string json = await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
			{
				Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
			};
			PrepareComfyUiRequest(request);
			using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
			string responseText = await response.Content.ReadAsStringAsync(token);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"ComfyUI 调用失败：{response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
			}
			return responseText;
		}, "提交任务", cancellationToken);
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		if (!jsonDocument.RootElement.TryGetProperty("prompt_id", out var promptIdElement) || promptIdElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(promptIdElement.GetString()))
		{
			throw new InvalidOperationException("ComfyUI 未返回 prompt_id。");
		}
		return promptIdElement.GetString();
	}

	private static async Task NormalizeComfyUiWorkflowModelNamesAsync(string baseUrl, JsonObject workflow, CancellationToken cancellationToken)
	{
		try
		{
			Uri endpoint = new Uri(new Uri(AppendTrailingSlash(baseUrl)), "object_info");
			string json = await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
			{
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, endpoint);
				PrepareComfyUiRequest(request);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
				string responseText = await response.Content.ReadAsStringAsync(token);
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"ComfyUI 模型列表读取失败：{response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
				}

				return responseText;
			}, "读取模型列表", cancellationToken);

			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			NormalizeComfyUiWorkflowModelNames(workflow, jsonDocument.RootElement);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
			(ex is HttpRequestException || ex is JsonException || ex is InvalidOperationException || ex is TaskCanceledException))
		{
		}
	}

	private static void NormalizeComfyUiWorkflowModelNames(JsonObject workflow, JsonElement objectInfo)
	{
		foreach (KeyValuePair<string, JsonNode?> nodeEntry in workflow.ToList())
		{
			if (nodeEntry.Value is not JsonObject nodeObject ||
				nodeObject["inputs"] is not JsonObject inputs ||
				!TryGetJsonString(nodeObject["class_type"], out string classType) ||
				string.IsNullOrWhiteSpace(classType) ||
				!objectInfo.TryGetProperty(classType, out JsonElement classInfo))
			{
				continue;
			}

			foreach (KeyValuePair<string, JsonNode?> inputEntry in inputs.ToList())
			{
				if (!TryGetJsonString(inputEntry.Value, out string currentValue) ||
					string.IsNullOrWhiteSpace(currentValue) ||
					!TryGetComfyUiAllowedInputValues(classInfo, inputEntry.Key, out List<string> allowedValues))
				{
					continue;
				}

				string? resolvedValue = ResolveComfyUiAllowedInputValue(currentValue, allowedValues);
				if (!string.IsNullOrWhiteSpace(resolvedValue) &&
					!string.Equals(resolvedValue, currentValue, StringComparison.Ordinal))
				{
					inputs[inputEntry.Key] = JsonValue.Create(resolvedValue);
				}
			}
		}
	}

	private static bool TryGetJsonString(JsonNode? node, out string value)
	{
		if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? text))
		{
			value = text ?? string.Empty;
			return true;
		}

		value = string.Empty;
		return false;
	}

	private static bool TryGetComfyUiAllowedInputValues(JsonElement classInfo, string inputName, out List<string> allowedValues)
	{
		allowedValues = new List<string>();
		if (!classInfo.TryGetProperty("input", out JsonElement inputInfo) || inputInfo.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		foreach (string sectionName in new[] { "required", "optional", "hidden" })
		{
			if (!inputInfo.TryGetProperty(sectionName, out JsonElement section) ||
				section.ValueKind != JsonValueKind.Object ||
				!section.TryGetProperty(inputName, out JsonElement inputConfig) ||
				inputConfig.ValueKind != JsonValueKind.Array ||
				inputConfig.GetArrayLength() == 0)
			{
				continue;
			}

			JsonElement firstConfig = inputConfig[0];
			if (firstConfig.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach (JsonElement option in firstConfig.EnumerateArray())
			{
				if (option.ValueKind == JsonValueKind.String)
				{
					string? optionText = option.GetString();
					if (!string.IsNullOrWhiteSpace(optionText))
					{
						allowedValues.Add(optionText);
					}
				}
			}

			break;
		}

		return allowedValues.Count > 0;
	}

	private static string? ResolveComfyUiAllowedInputValue(string currentValue, IReadOnlyList<string> allowedValues)
	{
		string? exact = allowedValues.FirstOrDefault(value => string.Equals(value, currentValue, StringComparison.Ordinal));
		if (!string.IsNullOrWhiteSpace(exact))
		{
			return exact;
		}

		string slashNormalized = currentValue.Replace('\\', '/');
		string? slashMatch = allowedValues.FirstOrDefault(value => string.Equals(value.Replace('\\', '/'), slashNormalized, StringComparison.Ordinal));
		if (!string.IsNullOrWhiteSpace(slashMatch))
		{
			return slashMatch;
		}

		string fileName = GetComfyUiModelFileName(currentValue);
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}

		string? fileNameMatch = allowedValues.FirstOrDefault(value => string.Equals(value, fileName, StringComparison.Ordinal)) ??
			allowedValues.FirstOrDefault(value => string.Equals(GetComfyUiModelFileName(value), fileName, StringComparison.Ordinal)) ??
			allowedValues.FirstOrDefault(value => string.Equals(value, currentValue, StringComparison.OrdinalIgnoreCase)) ??
			allowedValues.FirstOrDefault(value => string.Equals(value.Replace('\\', '/'), slashNormalized, StringComparison.OrdinalIgnoreCase)) ??
			allowedValues.FirstOrDefault(value => string.Equals(value, fileName, StringComparison.OrdinalIgnoreCase)) ??
			allowedValues.FirstOrDefault(value => string.Equals(GetComfyUiModelFileName(value), fileName, StringComparison.OrdinalIgnoreCase));

		return fileNameMatch;
	}

	private static string GetComfyUiModelFileName(string value)
	{
		string normalized = value.Trim().Replace('\\', '/');
		int lastSlash = normalized.LastIndexOf('/');
		return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
	}

	private static async Task<ComfyUiOutputImage> WaitForComfyUiImageAsync(string baseUrl, string promptId, CancellationToken cancellationToken)
	{
		Uri endpoint = new Uri(new Uri(AppendTrailingSlash(baseUrl)), "history/" + Uri.EscapeDataString(promptId));
		DateTime deadline = DateTime.UtcNow.AddMinutes(5.0);
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			JsonElement historyEntry;
			string json = await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
			{
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, endpoint);
				PrepareComfyUiRequest(request);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
				string responseText = await response.Content.ReadAsStringAsync(token);
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"读取 ComfyUI 历史记录失败：{response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
				}
				return responseText;
			}, "读取历史记录", cancellationToken);
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(json);
				if (TryGetComfyUiHistoryEntry(jsonDocument.RootElement, promptId, out historyEntry))
				{
					if (TryExtractComfyUiOutputImage(historyEntry, out var outputImage))
					{
						return outputImage;
					}
					if (historyEntry.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Object)
					{
						JsonElement statusValue;
						string statusText = ((statusElement.TryGetProperty("status_str", out statusValue) && statusValue.ValueKind == JsonValueKind.String) ? statusValue.GetString() : string.Empty);
						if (string.Equals(statusText, "error", StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidOperationException("ComfyUI 执行失败，请检查工作流和模型配置。");
						}
						if (statusElement.TryGetProperty("completed", out var completedValue) && completedValue.ValueKind == JsonValueKind.True)
						{
							throw new InvalidOperationException("ComfyUI 执行已结束，但没有返回图片输出。");
						}
						statusValue = default(JsonElement);
						completedValue = default(JsonElement);
						statusValue = default(JsonElement);
						completedValue = default(JsonElement);
					}
					outputImage = default(ComfyUiOutputImage);
					statusElement = default(JsonElement);
					outputImage = default(ComfyUiOutputImage);
					statusElement = default(JsonElement);
				}
				await Task.Delay(1000, cancellationToken);
			}
			historyEntry = default(JsonElement);
		}
		throw new TimeoutException("等待 ComfyUI 生成图片超时。");
	}

	private static bool TryGetComfyUiHistoryEntry(JsonElement root, string promptId, out JsonElement historyEntry)
	{
		if (root.ValueKind == JsonValueKind.Object)
		{
			if (root.TryGetProperty(promptId, out historyEntry))
			{
				return true;
			}
			if (root.TryGetProperty("outputs", out var _))
			{
				historyEntry = root;
				return true;
			}
		}
		historyEntry = default(JsonElement);
		return false;
	}

	private static bool TryExtractComfyUiOutputImage(JsonElement historyEntry, out ComfyUiOutputImage outputImage)
	{
		if (historyEntry.TryGetProperty("outputs", out var value) && value.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in value.EnumerateObject())
			{
				if (!item.Value.TryGetProperty("images", out var value2) || value2.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				foreach (JsonElement item2 in value2.EnumerateArray())
				{
					if (item2.ValueKind == JsonValueKind.Object)
					{
						JsonElement value3;
						string text = ((item2.TryGetProperty("filename", out value3) && value3.ValueKind == JsonValueKind.String) ? value3.GetString() : string.Empty);
						if (!string.IsNullOrWhiteSpace(text))
						{
							JsonElement value4;
							string subfolder = ((item2.TryGetProperty("subfolder", out value4) && value4.ValueKind == JsonValueKind.String) ? (value4.GetString() ?? string.Empty) : string.Empty);
							JsonElement value5;
							string type = ((item2.TryGetProperty("type", out value5) && value5.ValueKind == JsonValueKind.String) ? (value5.GetString() ?? "output") : "output");
							outputImage = new ComfyUiOutputImage(text, subfolder, type);
							return true;
						}
					}
				}
			}
		}
		outputImage = default(ComfyUiOutputImage);
		return false;
	}

	private static async Task<string> DownloadComfyUiImageAsync(WorkflowNode node, string baseUrl, ComfyUiOutputImage outputImage, CancellationToken cancellationToken)
	{
		StringBuilder viewUrl = new StringBuilder(AppendTrailingSlash(baseUrl) + "view?filename=" + Uri.EscapeDataString(outputImage.Filename));
		if (!string.IsNullOrWhiteSpace(outputImage.Subfolder))
		{
			viewUrl.Append("&subfolder=").Append(Uri.EscapeDataString(outputImage.Subfolder));
		}
		viewUrl.Append("&type=").Append(Uri.EscapeDataString(string.IsNullOrWhiteSpace(outputImage.Type) ? "output" : outputImage.Type));
		string extension = Path.GetExtension(outputImage.Filename);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".png";
		}
		string outputDirectory = EnsureOutputDirectory(node.Type);
		string filePath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
		await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, viewUrl.ToString());
			PrepareComfyUiRequest(request);
			using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
			response.EnsureSuccessStatusCode();
			await using Stream responseStream = await response.Content.ReadAsStreamAsync(token);
			await using FileStream outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
			await responseStream.CopyToAsync(outputStream, token);
			await outputStream.FlushAsync(token);
			return true;
		}, "下载图片", cancellationToken);
		return filePath;
	}

	private static async Task<GeneratedArtifact> ExecuteVideoTaskAsync(ModelInfo model, WorkflowNode node, string prompt, string? referenceImagePath, CancellationToken cancellationToken)
	{
		EnsureLocalOnlyModel(model, "视频模型");
		string negativePrompt = node.Params?.StoryboardVideoNegativePrompt ?? string.Empty;
		if (string.Equals(node.Type, "分镜视频", StringComparison.OrdinalIgnoreCase))
		{
			negativePrompt = string.Join(", ", new[] { WorkflowExecutor.BuildStoryboardVideoNegativePrompt(), negativePrompt }
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Select(value => value.Trim().Trim(',')));
		}
		if (IsComfyUiLike(model.Url))
		{
			return await ExecuteComfyUiImageTextToVideoAsync(model, node, prompt, negativePrompt, referenceImagePath, cancellationToken);
		}
		if (IsYunWuLike(model.Url))
		{
			return await ExecuteYunWuVideoTaskAsync(model, node, prompt, referenceImagePath, cancellationToken);
		}
		return await ExecuteGenericVideoTaskAsync(model, node, prompt, referenceImagePath, cancellationToken);
	}

	private static async Task<GeneratedArtifact> ExecuteComfyUiImageTextToVideoAsync(ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, string? referenceImagePath, CancellationToken cancellationToken)
	{
		PublishPrompt(node.Type + "/ComfyUI视频", model, prompt, negativePrompt);
		string? promptId = null;
		try
		{
			if (string.IsNullOrWhiteSpace(referenceImagePath) || !File.Exists(referenceImagePath))
			{
				throw new InvalidOperationException("本地图文生视频需要有效的参考图片。");
			}
			string baseUrl = NormalizeComfyUiBaseUrl(model.Url);
			string? uploadedReferenceImageName = await UploadComfyUiReferenceImageAsync(baseUrl, new string[1] { referenceImagePath }, cancellationToken);
			if (string.IsNullOrWhiteSpace(uploadedReferenceImageName))
			{
				throw new InvalidOperationException("上传本地图文生视频参考图失败。");
			}
			string workflowTemplateFileName = ResolveConfiguredComfyUiVideoWorkflowTemplateFileName(model);
			JsonObject workflow = LoadComfyUiWorkflowTemplate(workflowTemplateFileName);
			(int width, int height) = GetComfyUiVideoDimensions(node, referenceImagePath);
			string baseFilePrefix = IsDirectVideoNode(node)
				? (!string.IsNullOrWhiteSpace(node.Params?.DirectVideoFilenamePrefix) ? node.Params.DirectVideoFilenamePrefix : "video/LTX_2.3_i2v")
				: "video_" + node.Id;
			ConfigureComfyUiImageTextToVideoWorkflow(workflow, model, node, prompt, negativePrompt, width, height, baseFilePrefix, BuildStableSeed(node.Id, prompt, referenceImagePath), uploadedReferenceImageName);
			promptId = await SubmitComfyUiPromptAsync(baseUrl, workflow, cancellationToken);
			ModelCallLogService.LogSuccess(node.Type + "/ComfyUI视频提交", model, null, $"已提交 ComfyUI prompt_id：{promptId}；尺寸：{width}x{height}；参考图：{Path.GetFileName(referenceImagePath)}");
			ComfyUiOutputVideo outputVideo = await WaitForComfyUiVideoAsync(baseUrl, promptId, GetComfyUiVideoWaitTimeout(node), cancellationToken);
			string filePath = await DownloadComfyUiVideoAsync(node, baseUrl, outputVideo, cancellationToken);
			ModelCallLogService.LogSuccess(node.Type + "/ComfyUI视频下载", model, null, $"已回收视频：{Path.GetFileName(filePath)}");
			return new GeneratedArtifact(filePath, "已使用本地 ComfyUI 图文生视频工作流生成视频。");
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			ModelCallLogService.LogFailure(node.Type + "/ComfyUI视频", model, ex.Message, null, $"prompt_id：{promptId.OrDefault("未提交")}；参考图：{Path.GetFileName(referenceImagePath ?? string.Empty)}");
			throw;
		}
	}

	private static string ResolveConfiguredComfyUiVideoWorkflowTemplateFileName(ModelInfo model)
	{
		string configuredWorkflowJson = ModelConfig.ResolveComfyUiWorkflowJson(model);
		return string.IsNullOrWhiteSpace(configuredWorkflowJson)
			? ComfyUiImageTextToVideoTemplateFileName
			: configuredWorkflowJson;
	}

	private static (int Width, int Height) GetComfyUiVideoDimensions(WorkflowNode node, string? referenceImagePath)
	{
		if (TryGetVideoOrientationFromReferenceImage(referenceImagePath, out bool portraitFromReference))
		{
			return portraitFromReference ? (720, 1280) : (1280, 720);
		}

		if (IsDirectVideoNode(node))
		{
			bool portrait = string.Equals(node.Params?.DirectAspectRatio, "9:16", StringComparison.OrdinalIgnoreCase);
			return portrait ? (720, 1280) : (1280, 720);
		}

		string aspectRatio = GetStoryboardVideoAspectRatio(node);
		if (string.Equals(aspectRatio, "9:16", StringComparison.Ordinal))
		{
			return (720, 1280);
		}
		return (1280, 720);
	}

	private static bool TryGetVideoOrientationFromReferenceImage(string? referenceImagePath, out bool portrait)
	{
		portrait = false;
		if (string.IsNullOrWhiteSpace(referenceImagePath) || !File.Exists(referenceImagePath))
		{
			return false;
		}

		try
		{
			using Image image = Image.FromFile(referenceImagePath);
			portrait = image.Width <= image.Height;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void ConfigureComfyUiImageTextToVideoWorkflow(JsonObject workflow, ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, int width, int height, string filePrefix, long seed, string uploadedReferenceImageName)
	{
		ApplyComfyUiWorkflowPlaceholders(workflow, model, node, prompt, negativePrompt, width, height, filePrefix, seed, uploadedReferenceImageName);

		JsonObject? loadImageNode = FindWorkflowNodeByClassType(workflow, "LoadImage");
		if (loadImageNode?["inputs"] is JsonObject loadImageInputs)
		{
			loadImageInputs["image"] = JsonValue.Create(uploadedReferenceImageName);
		}
		JsonObject? promptNode = FindWorkflowNodeByClassType(workflow, "PrimitiveStringMultiline");
		if (promptNode?["inputs"] is JsonObject promptInputs)
		{
			promptInputs["value"] = JsonValue.Create(prompt);
		}
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (item.Value is JsonObject nodeObject &&
				string.Equals(nodeObject["class_type"]?.GetValue<string>(), "TextGenerateLTX2Prompt", StringComparison.OrdinalIgnoreCase) &&
				nodeObject["inputs"] is JsonObject textGenerateInputs &&
				textGenerateInputs.ContainsKey("prompt"))
			{
				textGenerateInputs["prompt"] = JsonValue.Create(prompt);
			}
		}
		JsonObject? negativeNode = FindComfyUiNegativeTextNode(workflow);
		if (negativeNode?["inputs"] is JsonObject negativeInputs)
		{
			string originalNegativePrompt = negativeInputs["text"]?.GetValue<string>() ?? string.Empty;
			negativeInputs["text"] = JsonValue.Create(BuildComfyUiNegativePrompt(originalNegativePrompt, negativePrompt));
		}
		SetComfyUiPositivePrompt(workflow, prompt, negativeNode);
		SetWorkflowPrimitiveIntByTitle(workflow, "Width", width);
		SetWorkflowPrimitiveIntByTitle(workflow, "Height", height);
		ApplyComfyUiVideoDimensionInputs(workflow, width, height);
		int fps = GetComfyUiVideoFrameRate(node);
		int duration = IsDirectVideoNode(node)
			? Math.Max(5, node.Params?.DirectDurationSeconds ?? 5)
			: GetStoryboardVideoDuration(node);
		SetWorkflowPrimitiveIntByTitle(workflow, "Frame Rate", fps);
		SetWorkflowPrimitiveIntByTitle(workflow, "Length", fps * duration);
		JsonObject? randomNoiseNode = FindWorkflowNodeByClassType(workflow, "RandomNoise");
		if (randomNoiseNode?["inputs"] is JsonObject randomNoiseInputs && randomNoiseInputs.ContainsKey("noise_seed"))
		{
			randomNoiseInputs["noise_seed"] = JsonValue.Create(seed);
		}
		JsonObject? saveVideoNode = FindWorkflowNodeByClassType(workflow, "SaveVideo");
		if (saveVideoNode?["inputs"] is JsonObject saveVideoInputs)
		{
			saveVideoInputs["filename_prefix"] = JsonValue.Create(BuildComfyUiFilePrefix(node, filePrefix));
		}
		if (ShouldOverrideComfyUiCheckpoint(model.Id))
		{
			JsonObject? checkpointNode = FindWorkflowNodeByClassType(workflow, "CheckpointLoaderSimple");
			if (checkpointNode?["inputs"] is JsonObject checkpointInputs && checkpointInputs.ContainsKey("ckpt_name"))
			{
				checkpointInputs["ckpt_name"] = JsonValue.Create(model.Id);
			}
			JsonObject? audioVaeNode = FindWorkflowNodeByClassType(workflow, "LTXVAudioVAELoader");
			if (audioVaeNode?["inputs"] is JsonObject audioVaeInputs && audioVaeInputs.ContainsKey("ckpt_name"))
			{
				audioVaeInputs["ckpt_name"] = JsonValue.Create(model.Id);
			}
			JsonObject? textEncoderNode = FindWorkflowNodeByClassType(workflow, "LTXAVTextEncoderLoader");
			if (textEncoderNode?["inputs"] is JsonObject textEncoderInputs && textEncoderInputs.ContainsKey("ckpt_name"))
			{
				textEncoderInputs["ckpt_name"] = JsonValue.Create(model.Id);
			}
		}
		NormalizeComfyUiLtxVideoLoras(workflow);
	}

	private static void NormalizeComfyUiLtxVideoLoras(JsonObject workflow)
	{
		bool usesLtx19b = workflow.Any(item =>
			item.Value is JsonObject nodeObject &&
			nodeObject["inputs"] is JsonObject inputs &&
			inputs.TryGetPropertyValue("ckpt_name", out JsonNode? checkpointNode) &&
			TryGetJsonString(checkpointNode, out string checkpointName) &&
			checkpointName.Contains("ltx-2-19b", StringComparison.OrdinalIgnoreCase));
		bool usesLtx22b = workflow.Any(item =>
			item.Value is JsonObject nodeObject &&
			nodeObject["inputs"] is JsonObject inputs &&
			inputs.TryGetPropertyValue("ckpt_name", out JsonNode? checkpointNode) &&
			TryGetJsonString(checkpointNode, out string checkpointName) &&
			checkpointName.Contains("ltx-2.3-22b", StringComparison.OrdinalIgnoreCase));
		if (!usesLtx19b && !usesLtx22b)
		{
			return;
		}

		foreach (KeyValuePair<string, JsonNode?> item in workflow.ToList())
		{
			if (item.Value is not JsonObject nodeObject ||
				nodeObject["inputs"] is not JsonObject inputs ||
				!inputs.TryGetPropertyValue("lora_name", out JsonNode? loraNode) ||
				!TryGetJsonString(loraNode, out string loraName))
			{
				continue;
			}

			if (usesLtx19b && loraName.Contains("ltx-2.3-22b-distilled-lora-384", StringComparison.OrdinalIgnoreCase))
			{
				inputs["lora_name"] = JsonValue.Create("ltx-2-19b-distilled-lora-384.safetensors");
			}
			else if (usesLtx22b && loraName.Contains("ltx-2-19b-distilled-lora-384", StringComparison.OrdinalIgnoreCase))
			{
				inputs["lora_name"] = JsonValue.Create("ltx-2.3-22b-distilled-lora-384.safetensors");
			}
		}
	}

	private static void SetComfyUiPositivePrompt(JsonObject workflow, string prompt, JsonObject? negativeNode)
	{
		JsonObject? positiveNode = FindComfyUiPositiveTextNode(workflow, negativeNode);
		if (positiveNode?["inputs"] is JsonObject positiveInputs && positiveInputs.ContainsKey("text"))
		{
			positiveInputs["text"] = JsonValue.Create(prompt);
		}
	}

	private static JsonObject? FindComfyUiNegativeTextNode(JsonObject workflow)
	{
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (item.Value is not JsonObject nodeObject ||
				!string.Equals(nodeObject["class_type"]?.GetValue<string>(), "LTXVConditioning", StringComparison.OrdinalIgnoreCase) ||
				nodeObject["inputs"] is not JsonObject nodeInputs)
			{
				continue;
			}

			JsonObject? negativeNode = ResolveConnectedWorkflowNode(workflow, nodeInputs, "negative");
			if (negativeNode?["inputs"] is JsonObject negativeInputs && negativeInputs.ContainsKey("text"))
			{
				return negativeNode;
			}
		}

		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (!(item.Value is JsonObject nodeObject) || !string.Equals(nodeObject["class_type"]?.GetValue<string>(), "CLIPTextEncode", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (!(nodeObject["inputs"] is JsonObject nodeInputs))
			{
				continue;
			}
			string text = nodeInputs["text"]?.GetValue<string>() ?? string.Empty;
			if (LooksLikeNegativePromptText(text))
			{
				return nodeObject;
			}
		}

		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (!(item.Value is JsonObject nodeObject) || !string.Equals(nodeObject["class_type"]?.GetValue<string>(), "CLIPTextEncode", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (!(nodeObject["inputs"] is JsonObject nodeInputs))
			{
				continue;
			}
			string text = nodeInputs["text"]?.GetValue<string>() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(text))
			{
				return nodeObject;
			}
		}
		return null;
	}

	private static JsonObject? FindComfyUiPositiveTextNode(JsonObject workflow, JsonObject? negativeNode)
	{
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (item.Value is not JsonObject nodeObject ||
				!string.Equals(nodeObject["class_type"]?.GetValue<string>(), "LTXVConditioning", StringComparison.OrdinalIgnoreCase) ||
				nodeObject["inputs"] is not JsonObject nodeInputs)
			{
				continue;
			}

			JsonObject? positiveNode = ResolveConnectedWorkflowNode(workflow, nodeInputs, "positive");
			if (IsComfyUiTextEncodeNode(positiveNode, negativeNode))
			{
				return positiveNode;
			}
		}

		JsonObject? fallback = null;
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (item.Value is not JsonObject nodeObject || !IsComfyUiTextEncodeNode(nodeObject, negativeNode) || nodeObject["inputs"] is not JsonObject nodeInputs)
			{
				continue;
			}

			string text = nodeInputs["text"]?.GetValue<string>() ?? string.Empty;
			if (!LooksLikeNegativePromptText(text))
			{
				return nodeObject;
			}

			fallback ??= nodeObject;
		}

		return fallback;
	}

	private static bool IsComfyUiTextEncodeNode(JsonObject? nodeObject, JsonObject? negativeNode)
	{
		return nodeObject != null &&
			(negativeNode == null || !ReferenceEquals(nodeObject, negativeNode)) &&
			string.Equals(nodeObject["class_type"]?.GetValue<string>(), "CLIPTextEncode", StringComparison.OrdinalIgnoreCase) &&
			nodeObject["inputs"] is JsonObject nodeInputs &&
			nodeInputs.ContainsKey("text");
	}

	private static bool LooksLikeNegativePromptText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string normalized = text.ToLowerInvariant();
		return normalized.Contains("bad anatomy", StringComparison.Ordinal) ||
			normalized.Contains("low quality", StringComparison.Ordinal) ||
			normalized.Contains("watermark", StringComparison.Ordinal) ||
			normalized.Contains("deformed", StringComparison.Ordinal) ||
			normalized.Contains("blurry", StringComparison.Ordinal) ||
			normalized.Contains("extra fingers", StringComparison.Ordinal) ||
			normalized.Contains("duplicate", StringComparison.Ordinal);
	}

	private static void ApplyComfyUiVideoDimensionInputs(JsonObject workflow, int width, int height)
	{
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (item.Value is not JsonObject nodeObject || nodeObject["inputs"] is not JsonObject nodeInputs)
			{
				continue;
			}

			SetNumericJsonInputIfPresent(nodeInputs, "width", width);
			SetNumericJsonInputIfPresent(nodeInputs, "height", height);
			SetNumericJsonInputIfPresent(nodeInputs, "resize_type.width", width);
			SetNumericJsonInputIfPresent(nodeInputs, "resize_type.height", height);
			if (string.Equals(nodeObject["class_type"]?.GetValue<string>(), "ImageScaleBy", StringComparison.OrdinalIgnoreCase))
			{
				PreventComfyUiVideoDownscale(nodeInputs);
			}
		}
	}

	private static void PreventComfyUiVideoDownscale(JsonObject inputs)
	{
		if (!inputs.TryGetPropertyValue("scale_by", out JsonNode? scaleNode) || scaleNode is not JsonValue scaleValue)
		{
			return;
		}

		if (scaleValue.TryGetValue<double>(out double scale) && scale < 1.0)
		{
			inputs["scale_by"] = JsonValue.Create(1.0);
		}
	}

	private static void SetNumericJsonInputIfPresent(JsonObject inputs, string inputName, int value)
	{
		if (!inputs.TryGetPropertyValue(inputName, out JsonNode? currentValue) || currentValue is not JsonValue jsonValue)
		{
			return;
		}

		if (jsonValue.TryGetValue<int>(out _) || jsonValue.TryGetValue<long>(out _) || jsonValue.TryGetValue<double>(out _))
		{
			inputs[inputName] = JsonValue.Create(value);
		}
	}

	private static void SetWorkflowPrimitiveIntByTitle(JsonObject workflow, string title, int value)
	{
		foreach (KeyValuePair<string, JsonNode> item in workflow)
		{
			if (!(item.Value is JsonObject nodeObject))
			{
				continue;
			}
			if (!string.Equals(nodeObject["class_type"]?.GetValue<string>(), "PrimitiveInt", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string nodeTitle = nodeObject["_meta"]?["title"]?.GetValue<string>() ?? string.Empty;
			if (string.Equals(nodeTitle, title, StringComparison.OrdinalIgnoreCase) && nodeObject["inputs"] is JsonObject nodeInputs)
			{
				nodeInputs["value"] = JsonValue.Create(value);
				return;
			}
		}
	}

	private static int GetComfyUiVideoFrameRate(WorkflowNode node)
	{
		if (IsDirectVideoNode(node))
		{
			return Math.Clamp(node.Params?.DirectVideoFrameRate ?? 25, 8, 60);
		}

		return Math.Clamp(node.Params?.StoryboardVideoFrameRate ?? 25, 8, 30);
	}

	private static TimeSpan GetComfyUiVideoWaitTimeout(WorkflowNode node)
	{
		return IsDirectVideoNode(node) ? TimeSpan.FromMinutes(45.0) : TimeSpan.FromMinutes(60.0);
	}

	private static bool IsDirectVideoNode(WorkflowNode node)
	{
		return string.Equals(node.Type, WorkflowNodeCatalog.TextToVideo, StringComparison.Ordinal) ||
			string.Equals(node.Type, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal);
	}

	private static int MakeEven(int value)
	{
		return (value % 2 == 0) ? value : (value - 1);
	}

	private static async Task<ComfyUiOutputVideo> WaitForComfyUiVideoAsync(string baseUrl, string promptId, TimeSpan timeout, CancellationToken cancellationToken)
	{
		Uri endpoint = new Uri(new Uri(AppendTrailingSlash(baseUrl)), "history/" + Uri.EscapeDataString(promptId));
		DateTime deadline = DateTime.UtcNow.Add(timeout);
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string json = await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
			{
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, endpoint);
				PrepareComfyUiRequest(request);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
				string responseText = await response.Content.ReadAsStringAsync(token);
				if (!response.IsSuccessStatusCode)
				{
					throw new InvalidOperationException($"读取 ComfyUI 视频历史失败：{response.StatusCode} {response.ReasonPhrase} {responseText}".Trim());
				}
				return responseText;
			}, "读取视频历史记录", cancellationToken);
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (TryGetComfyUiHistoryEntry(jsonDocument.RootElement, promptId, out var historyEntry))
			{
				if (TryExtractComfyUiOutputVideo(historyEntry, out var outputVideo))
				{
					return outputVideo;
				}
				if (historyEntry.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Object)
				{
					string statusText = ((statusElement.TryGetProperty("status_str", out var statusValue) && statusValue.ValueKind == JsonValueKind.String) ? statusValue.GetString() : string.Empty);
					if (string.Equals(statusText, "error", StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("ComfyUI 图文生视频执行失败，请检查工作流和模型配置。");
					}
					if (statusElement.TryGetProperty("completed", out var completedValue) && completedValue.ValueKind == JsonValueKind.True)
					{
						throw new InvalidOperationException("ComfyUI 视频执行已结束，但没有返回视频输出。");
					}
				}
			}
			await Task.Delay(1500, cancellationToken);
		}
		throw new TimeoutException($"等待 ComfyUI 生成视频超时（prompt_id：{promptId}，已等待约 {Math.Max(1, (int)Math.Round(timeout.TotalMinutes))} 分钟）。ComfyUI 后台任务可能仍在继续，请稍后通过任务历史或输出目录回收。");
	}

	private static bool TryExtractComfyUiOutputVideo(JsonElement historyEntry, out ComfyUiOutputVideo outputVideo)
	{
		if (historyEntry.TryGetProperty("outputs", out var outputsElement) && outputsElement.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty nodeProperty in outputsElement.EnumerateObject())
			{
				if (TryExtractComfyUiOutputVideoFromElement(nodeProperty.Value, out outputVideo))
				{
					return true;
				}
			}
		}

		outputVideo = default(ComfyUiOutputVideo);
		return false;
	}

	private static bool TryExtractComfyUiOutputVideoFromElement(JsonElement element, out ComfyUiOutputVideo outputVideo)
	{
		switch (element.ValueKind)
		{
		case JsonValueKind.Object:
			if (TryCreateComfyUiOutputVideo(element, out outputVideo))
			{
				return true;
			}

			foreach (string propertyName in new[] { "videos", "video", "gifs", "gif", "images", "image", "animated", "animations", "files", "file", "outputs", "ui" })
			{
				if (element.TryGetProperty(propertyName, out var nestedElement) && TryExtractComfyUiOutputVideoFromElement(nestedElement, out outputVideo))
				{
					return true;
				}
			}

			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (TryExtractComfyUiOutputVideoFromElement(property.Value, out outputVideo))
				{
					return true;
				}
			}
			break;
		case JsonValueKind.Array:
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (TryExtractComfyUiOutputVideoFromElement(item, out outputVideo))
				{
					return true;
				}
			}
			break;
		case JsonValueKind.String:
			if (TryCreateComfyUiOutputVideo(element.GetString(), string.Empty, "output", out outputVideo))
			{
				return true;
			}
			break;
		}

		outputVideo = default(ComfyUiOutputVideo);
		return false;
	}

	private static bool TryExtractComfyUiOutputVideoFromArray(JsonElement nodeOutput, string propertyName, out ComfyUiOutputVideo outputVideo)
	{
		if (nodeOutput.TryGetProperty(propertyName, out var arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in arrayElement.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				if (TryCreateComfyUiOutputVideo(item, out outputVideo))
				{
					return true;
				}
			}
		}
		outputVideo = default(ComfyUiOutputVideo);
		return false;
	}

	private static bool TryCreateComfyUiOutputVideo(JsonElement item, out ComfyUiOutputVideo outputVideo)
	{
		string filename = ReadDirectStringProperty(item, "filename", "file", "name", "path", "fullpath", "full_path", "video", "url");
		string subfolder = ReadDirectStringProperty(item, "subfolder", "folder", "sub_folder");
		string type = ReadDirectStringProperty(item, "type").OrDefault("output");
		return TryCreateComfyUiOutputVideo(filename, subfolder, type, out outputVideo);
	}

	private static bool TryCreateComfyUiOutputVideo(string? filename, string? subfolder, string? type, out ComfyUiOutputVideo outputVideo)
	{
		outputVideo = default(ComfyUiOutputVideo);
		string text = (filename ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text) || !LooksLikeVideoOutputPath(text))
		{
			return false;
		}

		string normalizedSubfolder = (subfolder ?? string.Empty).Trim().Trim('/', '\\');
		string normalizedFileName = text.Trim();
		if (!Uri.TryCreate(normalizedFileName, UriKind.Absolute, out _) && !Path.IsPathRooted(normalizedFileName))
		{
			string slashNormalized = normalizedFileName.Replace('\\', '/').Trim('/');
			int slashIndex = slashNormalized.LastIndexOf('/');
			if (slashIndex > 0 && string.IsNullOrWhiteSpace(normalizedSubfolder))
			{
				normalizedSubfolder = slashNormalized[..slashIndex].Trim('/');
				normalizedFileName = slashNormalized[(slashIndex + 1)..];
			}
		}

		outputVideo = new ComfyUiOutputVideo(normalizedFileName, normalizedSubfolder, string.IsNullOrWhiteSpace(type) ? "output" : type.Trim());
		return true;
	}

	private static string ReadDirectStringProperty(JsonElement item, params string[] propertyNames)
	{
		if (item.ValueKind != JsonValueKind.Object)
		{
			return string.Empty;
		}

		foreach (string propertyName in propertyNames)
		{
			if (item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
			{
				return value.GetString() ?? string.Empty;
			}
		}

		return string.Empty;
	}

	private static bool LooksLikeVideoOutputPath(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		int queryIndex = text.IndexOfAny(new[] { '?', '#' });
		string withoutQuery = queryIndex >= 0 ? text[..queryIndex] : text;
		string extension = Path.GetExtension(withoutQuery).ToLowerInvariant();
		return extension is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".gif";
	}

	private static async Task<string> DownloadComfyUiVideoAsync(WorkflowNode node, string baseUrl, ComfyUiOutputVideo outputVideo, CancellationToken cancellationToken)
	{
		string extension = GetVideoOutputExtension(outputVideo.Filename);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".mp4";
		}
		string outputDirectory = EnsureOutputDirectory(node.Type);
		string filePath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");

		if (File.Exists(outputVideo.Filename))
		{
			File.Copy(outputVideo.Filename, filePath, true);
			return filePath;
		}

		string downloadUrl;
		if (Uri.TryCreate(outputVideo.Filename, UriKind.Absolute, out Uri? directUri) &&
			(directUri.Scheme == Uri.UriSchemeHttp || directUri.Scheme == Uri.UriSchemeHttps))
		{
			downloadUrl = directUri.ToString();
		}
		else
		{
			StringBuilder viewUrl = new StringBuilder(AppendTrailingSlash(baseUrl) + "view?filename=" + Uri.EscapeDataString(outputVideo.Filename));
			if (!string.IsNullOrWhiteSpace(outputVideo.Subfolder))
			{
				viewUrl.Append("&subfolder=").Append(Uri.EscapeDataString(outputVideo.Subfolder));
			}
			viewUrl.Append("&type=").Append(Uri.EscapeDataString(string.IsNullOrWhiteSpace(outputVideo.Type) ? "output" : outputVideo.Type));
			downloadUrl = viewUrl.ToString();
		}

		await ExecuteComfyUiWithRetryAsync(async (CancellationToken token) =>
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
			PrepareComfyUiRequest(request);
			using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
			response.EnsureSuccessStatusCode();
			await using Stream responseStream = await response.Content.ReadAsStreamAsync(token);
			await using FileStream outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
			await responseStream.CopyToAsync(outputStream, token);
			await outputStream.FlushAsync(token);
			return true;
		}, "下载视频", cancellationToken);
		return filePath;
	}

	private static string GetVideoOutputExtension(string value)
	{
		string text = (value ?? string.Empty).Trim();
		int queryIndex = text.IndexOfAny(new[] { '?', '#' });
		string withoutQuery = queryIndex >= 0 ? text[..queryIndex] : text;
		string extension = Path.GetExtension(withoutQuery);
		return string.IsNullOrWhiteSpace(extension) ? string.Empty : extension;
	}

	private static async Task<T> ExecuteComfyUiWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName, CancellationToken cancellationToken)
	{
		TimeSpan[] retryDelays = new TimeSpan[4]
		{
			TimeSpan.FromMilliseconds(500.0),
			TimeSpan.FromMilliseconds(1200.0),
			TimeSpan.FromMilliseconds(2200.0),
			TimeSpan.FromMilliseconds(3500.0)
		};
		Exception? lastException = null;
		for (int attempt = 0; attempt <= retryDelays.Length; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				return await operation(cancellationToken);
			}
			catch (Exception ex) when (IsComfyUiTransientTransportException(ex) && attempt < retryDelays.Length)
			{
				lastException = ex;
				await Task.Delay(retryDelays[attempt], cancellationToken);
			}
			catch (Exception ex) when (IsComfyUiTransientTransportException(ex))
			{
				lastException = ex;
				break;
			}
		}
		throw new InvalidOperationException($"ComfyUI {operationName} 失败，连接被对端提前断开，请重试。原始错误：{lastException?.Message}", lastException);
	}

	private static void PrepareComfyUiRequest(HttpRequestMessage request)
	{
		request.Version = HttpVersion.Version11;
		request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
		request.Headers.ConnectionClose = true;
	}

	private static bool IsComfyUiTransientTransportException(Exception exception)
	{
		for (Exception ex = exception; ex != null; ex = ex.InnerException)
		{
			if (ex is IOException || ex is HttpRequestException || ex is SocketException)
			{
				return true;
			}
			string? fullName = ex.GetType().FullName;
			if (!string.IsNullOrWhiteSpace(fullName) && (fullName.Contains("HttpIOException", StringComparison.OrdinalIgnoreCase) || fullName.Contains("HttpProtocolException", StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
			if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static async Task<GeneratedArtifact> ExecuteStableDiffusionImageAsync(ModelInfo model, WorkflowNode node, string prompt, CancellationToken cancellationToken, int? widthOverride = null, int? heightOverride = null, string? negativePrompt = null, string? moduleName = null, long? seed = null)
	{
		PublishPrompt(moduleName ?? node.Type + "/StableDiffusion图片", model, prompt, negativePrompt);
		string baseUrl = NormalizeStableDiffusionBaseUrl(model.Url);
		await TrySwitchStableDiffusionModelAsync(model, baseUrl, cancellationToken);
		int width = widthOverride ?? GetImageCanvasSize(node).Width;
		int height = heightOverride ?? GetImageCanvasSize(node).Height;
		var requestBody = new
		{
			prompt = prompt,
			negative_prompt = negativePrompt,
			steps = 28,
			cfg_scale = 7,
			width = width,
			height = height,
			sampler_name = "DPM++ 2M Karras",
			seed = seed ?? -1
		};
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(AppendTrailingSlash(baseUrl)), "txt2img"))
		{
			Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
		};
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"图片模型调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		if (!jsonDocument.RootElement.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array || imagesElement.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("Stable Diffusion 未返回图片。");
		}
		string base64 = imagesElement[0].GetString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(base64))
		{
			throw new InvalidOperationException("Stable Diffusion 返回的图片内容为空。");
		}
		string filePath = SaveBase64Artifact(node, base64, "png");
		ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement));
		return new GeneratedArtifact(filePath, "已生成图片文件，可继续用于下游节点。");
	}

	private static async Task<GeneratedArtifact> ExecuteStableDiffusionImageToImageAsync(ModelInfo model, WorkflowNode node, string prompt, string referenceImagePath, CancellationToken cancellationToken, int width, int height, string? negativePrompt = null, string? moduleName = null, long? seed = null, double denoisingStrength = 0.82)
	{
		PublishPrompt(moduleName ?? node.Type + "/StableDiffusion图生图", model, prompt, negativePrompt);
		if (string.IsNullOrWhiteSpace(referenceImagePath) || !File.Exists(referenceImagePath))
		{
			return await ExecuteStableDiffusionImageAsync(model, node, prompt, cancellationToken, width, height, negativePrompt, moduleName, seed);
		}

		string baseUrl = NormalizeStableDiffusionBaseUrl(model.Url);
		await TrySwitchStableDiffusionModelAsync(model, baseUrl, cancellationToken);
		var (referenceBase64, _) = ReadInlineImageData(referenceImagePath);
		var requestBody = new
		{
			init_images = new string[1] { referenceBase64 },
			prompt = prompt,
			negative_prompt = negativePrompt,
			steps = 30,
			cfg_scale = 7,
			width = width,
			height = height,
			sampler_name = "DPM++ 2M Karras",
			denoising_strength = Math.Clamp(denoisingStrength, 0.2, 1.0),
			resize_mode = 1,
			seed = seed ?? -1
		};
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(AppendTrailingSlash(baseUrl)), "img2img"))
		{
			Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
		};
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"图片模型Image-to-Image调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		if (!jsonDocument.RootElement.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array || imagesElement.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("Stable Diffusion Image-to-Image未返回图片。");
		}
		string base64 = imagesElement[0].GetString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(base64))
		{
			throw new InvalidOperationException("Stable Diffusion Image-to-Image返回的图片内容为空。");
		}
		string filePath = SaveBase64Artifact(node, base64, "png");
		ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), "已使用参考脸Image-to-Image");
		return new GeneratedArtifact(filePath, "已基于参考脸生成图片文件，可继续用于下游节点。");
	}

	private static async Task TrySwitchStableDiffusionModelAsync(ModelInfo model, string baseUrl, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(model.Id))
		{
			return;
		}
		var payload = new
		{
			sd_model_checkpoint = model.Id
		};
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(AppendTrailingSlash(baseUrl)), "options"))
		{
			Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
		};
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		await response.Content.ReadAsStringAsync(cancellationToken);
	}

	private static async Task<GeneratedArtifact> ExecuteOpenAiCompatibleImageAsync(ModelInfo model, WorkflowNode node, string prompt, CancellationToken cancellationToken, string? sizeOverride = null, string? moduleName = null, string? negativePrompt = null)
	{
		EnsureLocalOnlyModel(model, "OpenAI 兼容图片模型");
		PublishPrompt(moduleName ?? node.Type + "/OpenAI兼容图片", model, prompt, negativePrompt);
		Uri endpoint = ResolveOpenAiImageEndpoint(model.Url, useEditEndpoint: false);
		Dictionary<string, object?> requestBody = new Dictionary<string, object?>
		{
			["model"] = model.Id,
			["prompt"] = prompt,
			["size"] = string.IsNullOrWhiteSpace(sizeOverride) ? GetImageCanvasSizeText(node) : sizeOverride,
			["response_format"] = "b64_json"
		};
		if (!string.IsNullOrWhiteSpace(negativePrompt))
		{
			requestBody["negative_prompt"] = negativePrompt;
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
		};
		ApplyAuthorizationHeader(request, model);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"图片模型调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), "云调用成功，已收到响应");
		if (TryFindString(jsonDocument.RootElement, new string[1] { "b64_json" }, out var base64))
		{
			string filePath = SaveBase64Artifact(node, base64, "png");
			return new GeneratedArtifact(filePath, "已生成图片文件，可继续用于下游节点。");
		}
		if (TryFindString(jsonDocument.RootElement, new string[1] { "url" }, out var imageUrl))
		{
			return new GeneratedArtifact(await DownloadArtifactAsync(node, imageUrl, "png", cancellationToken), "已生成图片文件，可继续用于下游节点。");
		}
		throw new InvalidOperationException("图片模型返回格式无法识别。");
	}

	private static async Task<GeneratedArtifact> ExecuteOpenAiCompatibleImageEditAsync(ModelInfo model, WorkflowNode node, string prompt, string referenceImagePath, CancellationToken cancellationToken, string? sizeOverride = null, string? moduleName = null)
	{
		if (string.IsNullOrWhiteSpace(referenceImagePath) || !File.Exists(referenceImagePath))
		{
			return await ExecuteOpenAiCompatibleImageAsync(model, node, prompt, cancellationToken, sizeOverride, moduleName);
		}

		PublishPrompt(moduleName ?? node.Type + "/OpenAI兼容图片编辑", model, prompt, "参考图：" + referenceImagePath);
		Uri endpoint = ResolveOpenAiImageEndpoint(model.Url, useEditEndpoint: true);
		using MultipartFormDataContent form = new MultipartFormDataContent();
		form.Add(new StringContent(model.Id ?? string.Empty, Encoding.UTF8), "model");
		form.Add(new StringContent(prompt ?? string.Empty, Encoding.UTF8), "prompt");
		form.Add(new StringContent(string.IsNullOrWhiteSpace(sizeOverride) ? GetImageCanvasSizeText(node) : sizeOverride, Encoding.UTF8), "size");
		form.Add(new StringContent("1", Encoding.UTF8), "n");
		form.Add(new StringContent("b64_json", Encoding.UTF8), "response_format");
		byte[] imageBytes = await File.ReadAllBytesAsync(referenceImagePath, cancellationToken);
		ByteArrayContent imageContent = new ByteArrayContent(imageBytes);
		imageContent.Headers.ContentType = new MediaTypeHeaderValue(ReadInlineImageData(referenceImagePath).MimeType);
		form.Add(imageContent, "image", Path.GetFileName(referenceImagePath));
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = form
		};
		ApplyAuthorizationHeader(request, model);
		using HttpResponseMessage response = IsYunWuLike(model.Url)
			? await SendYunWuRequestAsync(request, model.Url, cancellationToken)
			: await HttpClient.SendAsync(request, cancellationToken);
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"图片模型编辑调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
		}

		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), "已使用正面表情参考图生成三视图");
		if (TryFindString(jsonDocument.RootElement, new string[1] { "b64_json" }, out var base64))
		{
			string filePath = SaveBase64Artifact(node, base64, "png");
			return new GeneratedArtifact(filePath, "已根据正面表情参考图生成角色三视图。");
		}
		if (TryFindString(jsonDocument.RootElement, new string[1] { "url" }, out var imageUrl))
		{
			return new GeneratedArtifact(await DownloadArtifactAsync(node, imageUrl, "png", cancellationToken), "已根据正面表情参考图生成角色三视图。");
		}

		throw new InvalidOperationException("图片模型编辑返回格式无法识别。");
	}

	private static async Task<GeneratedArtifact> ExecuteYunWuImageAsync(ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, CancellationToken cancellationToken, string? sizeOverride = null, string? moduleName = null, IReadOnlyList<string>? referenceImagePaths = null)
	{
		PublishPrompt(moduleName ?? node.Type + "/云雾图片", model, prompt, negativePrompt);
		if (IsYunWuGeminiImageModel(model.Id))
		{
			return await ExecuteYunWuGeminiImageAsync(model, node, prompt, negativePrompt, cancellationToken, sizeOverride, moduleName, referenceImagePaths);
		}
		Uri endpoint = ResolveOpenAiImageEndpoint(model.Url, useEditEndpoint: false);
		Dictionary<string, object?> requestBody = new Dictionary<string, object?>
		{
			["model"] = model.Id,
			["prompt"] = prompt,
			["size"] = (string.IsNullOrWhiteSpace(sizeOverride) ? GetImageCanvasSizeText(node) : sizeOverride),
			["response_format"] = "url",
			["n"] = 1,
			["watermark"] = false
		};
		if (!string.IsNullOrWhiteSpace(negativePrompt))
		{
			requestBody["negative_prompt"] = negativePrompt;
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
		};
		ApplyAuthorizationHeader(request, model);
		using HttpResponseMessage response = await SendYunWuRequestAsync(request, model.Url, cancellationToken);
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"云雾图片模型调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), "云调用成功，已收到响应");
		if (TryFindString(jsonDocument.RootElement, new string[1] { "b64_json" }, out var base64))
		{
			string filePath = SaveBase64Artifact(node, base64, "png");
			return new GeneratedArtifact(filePath, "已通过云雾 API 生成图片文件，可继续用于下游节点。");
		}
		if (TryFindString(jsonDocument.RootElement, new string[1] { "url" }, out var imageUrl))
		{
			return new GeneratedArtifact(await DownloadArtifactAsync(node, imageUrl, "png", cancellationToken), "已通过云雾 API 生成图片文件，可继续用于下游节点。");
		}
		throw new InvalidOperationException("云雾图片模型返回格式无法识别。");
	}

	private static async Task<GeneratedArtifact> ExecuteYunWuGeminiImageAsync(ModelInfo model, WorkflowNode node, string prompt, string negativePrompt, CancellationToken cancellationToken, string? sizeOverride = null, string? moduleName = null, IReadOnlyList<string>? referenceImagePaths = null)
	{
		if (string.IsNullOrWhiteSpace(model.Key))
		{
			throw new InvalidOperationException("云雾 Gemini 图片模型缺少 API Key。");
		}
		(string, string) tuple = ResolveYunWuGeminiImageConfig(node, sizeOverride, moduleName);
		string aspectRatio = tuple.Item1;
		string resolution = tuple.Item2;
		string requestPrompt = prompt.Trim();
		List<object> parts = new List<object>
		{
			new
			{
				text = requestPrompt
			}
		};
		if (referenceImagePaths != null)
		{
			foreach (string path in referenceImagePaths.Where((string text) => !string.IsNullOrWhiteSpace(text) && File.Exists(text)).Distinct<string>(StringComparer.OrdinalIgnoreCase))
			{
				var (data, referenceMimeType) = ReadInlineImageData(path);
				parts.Add(new
				{
					inlineData = new
					{
						data = data,
						mimeType = referenceMimeType
					}
				});
			}
		}
		string endpoint = $"{NormalizeYunWuRootUrl(model.Url)}/v1beta/models/{Uri.EscapeDataString(model.Id)}:generateContent?key={Uri.EscapeDataString(model.Key)}";
		var requestBody = new
		{
			contents = new[]
			{
				new
				{
					role = "user",
					parts = parts.ToArray()
				}
			},
			generationConfig = new
			{
				responseModalities = new string[2] { "TEXT", "IMAGE" },
				imageConfig = new
				{
					aspectRatio = aspectRatio,
					imageSize = resolution
				},
				numberOfImages = 1
			}
		};
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
		};
		ApplyAuthorizationHeader(request, model);
		using HttpResponseMessage response = await SendYunWuRequestAsync(request, model.Url, cancellationToken);
		string json = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			ModelCallLogService.LogFailure(moduleName ?? node.Type, model, $"{response.StatusCode} {response.ReasonPhrase}".Trim(), json);
			throw new InvalidOperationException($"云雾 Gemini 图片调用失败：{response.StatusCode} {response.ReasonPhrase} {json}".Trim());
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		ModelCallLogService.LogSuccess(moduleName ?? node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), "云调用成功，已收到响应");
		if (TryExtractInlineImage(jsonDocument.RootElement, out var base64, out var mimeType))
		{
			string filePath = SaveBase64Artifact(extension: MimeTypeToExtension(mimeType), node: node, rawBase64: base64);
			return new GeneratedArtifact(filePath, "已通过云雾 Gemini 图片接口生成图片文件，可继续用于下游节点。");
		}
		ModelCallLogService.LogFailure(moduleName ?? node.Type, model, "返回中未找到可保存的图像数据。", json);
		throw new InvalidOperationException("云雾 Gemini 图片返回中未找到可保存的图像数据：" + json);
	}

	private static async Task<GeneratedArtifact> ExecuteVideoNodeAsync(ModelInfo model, WorkflowDocument document, WorkflowNode node, string input, CancellationToken cancellationToken)
	{
		bool isStoryboardVideo = string.Equals(node.Type, "分镜视频", StringComparison.OrdinalIgnoreCase);
		string storyboardPrompt = string.IsNullOrWhiteSpace(node.Params?.StoryboardVideoModelPrompt)
			? (node.Params?.StoryboardVideoPrompt ?? string.Empty)
			: node.Params.StoryboardVideoModelPrompt;
		string prompt = isStoryboardVideo && !string.IsNullOrWhiteSpace(storyboardPrompt)
			? storyboardPrompt
			: WorkflowExecutor.BuildVideoPrompt(node, input);
		string referenceImagePath = ((!string.Equals(node.Type, "分镜视频", StringComparison.OrdinalIgnoreCase)) ? WorkflowExecutor.CollectUpstreamArtifactPaths(document, node, "image").FirstOrDefault() : (node.Params?.StoryboardVideoFusedImagePath ?? string.Empty));
		if (IsYunWuLike(model.Url))
		{
			return await ExecuteYunWuVideoTaskAsync(model, node, prompt, referenceImagePath, cancellationToken);
		}
		return await ExecuteGenericVideoTaskAsync(model, node, prompt, referenceImagePath, cancellationToken);
	}

	private static async Task<GeneratedArtifact> ExecuteYunWuVideoTaskAsync(ModelInfo model, WorkflowNode node, string prompt, string? referenceImagePath, CancellationToken cancellationToken)
	{
		PublishPrompt(node.Type + "/云雾视频", model, prompt);
		string rootUrl = NormalizeYunWuRootUrl(model.Url);
		node.Params ??= new WorkflowNodeParameters();
		node.Params.StoryboardVideoTaskId = string.Empty;
		node.Params.StoryboardVideoTaskQueryUrl = string.Empty;
		node.Params.StoryboardVideoLastError = string.Empty;
		string referenceImage = GetYunWuVideoReferenceImage(referenceImagePath);
		List<Uri> submitEndpoints = BuildYunWuVideoSubmitEndpoints(model.Url, !string.IsNullOrWhiteSpace(referenceImage)).ToList();
		Exception lastError = null;
		foreach (Uri submitEndpoint in submitEndpoints)
		{
			try
			{
				string directUrl;
				string taskId;
				using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, submitEndpoint)
				{
					Content = new StringContent(JsonSerializer.Serialize(BuildYunWuVideoRequestBody(node, model, prompt, referenceImage)), Encoding.UTF8, "application/json")
				})
				{
					ApplyAuthorizationHeader(request, model);
					using (HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken))
					{
						string json = await response.Content.ReadAsStringAsync(cancellationToken);
						if (!response.IsSuccessStatusCode)
						{
							lastError = new InvalidOperationException($"云雾视频接口返回 {response.StatusCode} {response.ReasonPhrase} {json}".Trim());
							node.Params ??= new WorkflowNodeParameters();
							node.Params.StoryboardVideoLastError = lastError.Message;
							continue;
						}
						using JsonDocument jsonDocument = JsonDocument.Parse(json);
						if (TryFindString(jsonDocument.RootElement, new string[3] { "task_id", "taskId", "id" }, out taskId))
						{
							ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), "云调用成功，已收到视频任务");
							Uri statusUri = BuildYunWuVideoStatusUri(rootUrl, submitEndpoint.AbsolutePath, taskId);
							node.Params ??= new WorkflowNodeParameters();
							node.Params.StoryboardVideoTaskId = taskId;
							node.Params.StoryboardVideoTaskQueryUrl = statusUri.ToString();
							node.Params.StoryboardVideoLastError = string.Empty;
							return await PollYunWuVideoTaskAsync(model, node, statusUri, cancellationToken);
						}
						if (TryFindYunWuVideoUrl(jsonDocument.RootElement, out directUrl))
						{
							string artifactPath = await DownloadArtifactAsync(node, directUrl, "mp4", cancellationToken);
							ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), $"云调用成功，视频已下载：{Path.GetFileName(artifactPath)}");
							node.Params ??= new WorkflowNodeParameters();
							node.Params.StoryboardVideoTaskId = string.Empty;
							node.Params.StoryboardVideoTaskQueryUrl = string.Empty;
							node.Params.StoryboardVideoLastError = string.Empty;
							return new GeneratedArtifact(artifactPath, "已通过云雾 API 生成视频文件，可继续用于下游节点。");
						}
						lastError = new InvalidOperationException("云雾视频接口未返回可识别的 task_id 或 video_url。");
						node.Params ??= new WorkflowNodeParameters();
						node.Params.StoryboardVideoLastError = lastError.Message;
					}
					goto IL_05aa;
				}
				IL_05aa:
				directUrl = null;
				taskId = null;
				directUrl = null;
				taskId = null;
			}
			catch (Exception ex)
			{
				lastError = ex;
				node.Params ??= new WorkflowNodeParameters();
				node.Params.StoryboardVideoLastError = ex.Message;
			}
		}
		throw new InvalidOperationException(lastError?.Message ?? "云雾视频接口未返回可识别的任务信息。", lastError);
	}

	private static async Task<GeneratedArtifact?> TryResumeYunWuVideoTaskAsync(ModelInfo model, WorkflowNode node, string rootUrl, CancellationToken cancellationToken)
	{
		node.Params ??= new WorkflowNodeParameters();
		foreach (Uri queryCandidate in BuildYunWuVideoQueryCandidates(rootUrl, node.Params.StoryboardVideoTaskId, node.Params.StoryboardVideoTaskQueryUrl))
		{
			GeneratedArtifact? generatedArtifact = await TryDownloadCompletedYunWuVideoAsync(model, node, queryCandidate, cancellationToken);
			if (generatedArtifact.HasValue)
			{
				node.Params.StoryboardVideoTaskQueryUrl = queryCandidate.ToString();
				node.Params.StoryboardVideoLastError = string.Empty;
				return generatedArtifact.Value;
			}
		}
		return null;
	}

	private static async Task<GeneratedArtifact?> TryDownloadCompletedYunWuVideoAsync(ModelInfo model, WorkflowNode node, Uri statusUri, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, statusUri);
		ApplyAuthorizationHeader(request, model);
		using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}
		string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
		if (LooksLikeBinaryVideoContentType(mediaType))
		{
			byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
			if (bytes.Length == 0)
			{
				return null;
			}
			string artifactPath = await SaveDownloadedArtifactAsync(node, bytes, InferVideoExtension(mediaType, statusUri), cancellationToken);
			ModelCallLogService.LogSuccess(node.Type, model, null, $"云调用成功，已直接下载任务视频：{Path.GetFileName(artifactPath)}");
			return new GeneratedArtifact(artifactPath, "已从云雾视频任务直接下载视频文件，可继续用于下游节点。");
		}
		string payload = await response.Content.ReadAsStringAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}
		string directUrl = payload.Trim().Trim('"');
		if (LooksLikeVideoUrl(directUrl))
		{
			string artifactPath = await DownloadArtifactAsync(node, directUrl, "mp4", cancellationToken);
			ModelCallLogService.LogSuccess(node.Type, model, null, $"云调用成功，已根据任务查询下载视频：{Path.GetFileName(artifactPath)}");
			return new GeneratedArtifact(artifactPath, "已从云雾视频任务回收视频文件，可继续用于下游节点。");
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(payload);
			List<string> videoUrls = EnumerateYunWuVideoUrls(jsonDocument.RootElement);
			if (videoUrls.Count == 0)
			{
				return null;
			}

			Exception? lastDownloadError = null;
			foreach (string videoUrl in videoUrls)
			{
				try
				{
					string artifactPath2 = await DownloadArtifactAsync(node, videoUrl, "mp4", cancellationToken);
					ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), $"云调用成功，已从现有任务回收视频：{Path.GetFileName(artifactPath2)}");
					return new GeneratedArtifact(artifactPath2, "已从云雾视频任务回收视频文件，可继续用于下游节点。");
				}
				catch (Exception ex)
				{
					lastDownloadError = ex;
				}
			}

			if (lastDownloadError != null)
			{
				throw new InvalidOperationException("云雾已返回视频地址，但当前返回的候选地址均下载失败。", lastDownloadError);
			}

			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static async Task<GeneratedArtifact> ExecuteGenericVideoTaskAsync(ModelInfo model, WorkflowNode node, string prompt, string? referenceImagePath, CancellationToken cancellationToken)
	{
		PublishPrompt(node.Type + "/视频生成", model, prompt);
		string baseUrl = NormalizeApiBaseUrl(model.Url);
		string[] submitCandidates = new string[4] { "videos/generations", "video/generations", "videos", "video/submit" };
		string referenceImage = ((!string.IsNullOrWhiteSpace(referenceImagePath) && File.Exists(referenceImagePath)) ? ConvertFileToDataUrl(referenceImagePath) : null);
		string[] array = submitCandidates;
		for (int i = 0; i < array.Length; i++)
		{
			string directUrl;
			string statusUrl;
			string taskId;
			using (HttpRequestMessage request = new HttpRequestMessage(requestUri: new Uri(relativeUri: array[i], baseUri: new Uri(AppendTrailingSlash(baseUrl))), method: HttpMethod.Post)
			{
				Content = new StringContent(JsonSerializer.Serialize(BuildVideoRequestBody(node, model.Id, prompt, referenceImage)), Encoding.UTF8, "application/json")
			})
			{
				ApplyAuthorizationHeader(request, model);
				using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
				string json = await response.Content.ReadAsStringAsync(cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					continue;
				}
				using (JsonDocument jsonDocument = JsonDocument.Parse(json))
				{
					if (TryFindString(jsonDocument.RootElement, new string[2] { "video_url", "url" }, out directUrl))
					{
						return new GeneratedArtifact(await DownloadArtifactAsync(node, directUrl, "mp4", cancellationToken), "已生成视频文件，可继续用于下游节点。");
					}
					if (TryFindString(jsonDocument.RootElement, new string[1] { "status_url" }, out statusUrl))
					{
						return await PollGenericVideoTaskAsync(model, node, new Uri(statusUrl), cancellationToken);
					}
					if (TryFindString(jsonDocument.RootElement, new string[3] { "id", "task_id", "taskId" }, out taskId))
					{
						return await PollGenericVideoTaskAsync(model, node, BuildStatusUri(baseUrl, taskId), cancellationToken);
					}
				}
				goto IL_0578;
			}
			IL_0578:
			directUrl = null;
			statusUrl = null;
			taskId = null;
		}
		throw new InvalidOperationException("视频模型接口未返回可识别的任务信息。");
	}

	private static object BuildVideoRequestBody(WorkflowNode node, string modelId, string prompt, string? referenceImage)
	{
		int storyboardVideoDuration = GetStoryboardVideoDuration(node);
		string storyboardVideoAspectRatio = GetStoryboardVideoAspectRatio(node);
		if (string.IsNullOrWhiteSpace(referenceImage))
		{
			return new
			{
				model = modelId,
				prompt = prompt,
				seconds = storyboardVideoDuration,
				duration = storyboardVideoDuration,
				size = (string.Equals(storyboardVideoAspectRatio, "9:16", StringComparison.Ordinal) ? "720x1280" : "1280x720"),
				aspect_ratio = storyboardVideoAspectRatio
			};
		}
		return new
		{
			model = modelId,
			prompt = prompt,
			image = referenceImage,
			image_url = referenceImage,
			seconds = storyboardVideoDuration,
			duration = storyboardVideoDuration,
			size = (string.Equals(storyboardVideoAspectRatio, "9:16", StringComparison.Ordinal) ? "720x1280" : "1280x720"),
			aspect_ratio = storyboardVideoAspectRatio
		};
	}

	private static object BuildYunWuVideoRequestBody(WorkflowNode node, ModelInfo model, string prompt, string? referenceImage)
	{
		int storyboardVideoDuration = GetStoryboardVideoDuration(node);
		string storyboardVideoAspectRatio = GetStoryboardVideoAspectRatio(node);
		string storyboardVideoYunWuMode = GetStoryboardVideoYunWuMode(node);
		string storyboardVideoQualityCode = GetStoryboardVideoQualityCode(node);
		string storyboardVideoRequestedModelName = GetStoryboardVideoRequestedModelName(node, model);
		string sound = ((node.Params?.StoryboardVideoNeedSound ?? false) ? "on" : "off");
		if (string.IsNullOrWhiteSpace(referenceImage))
		{
			return new
			{
				model_name = storyboardVideoRequestedModelName,
				model = storyboardVideoRequestedModelName,
				model_family = (node.Params?.StoryboardVideoModelFamily ?? string.Empty),
				sub_model = (node.Params?.StoryboardVideoSubModel ?? string.Empty),
				prompt = prompt,
				quality = storyboardVideoQualityCode,
				mode = storyboardVideoYunWuMode,
				duration = storyboardVideoDuration,
				aspect_ratio = storyboardVideoAspectRatio,
				cfg_scale = 0.5,
				sound
			};
		}
		return new
		{
			model_name = storyboardVideoRequestedModelName,
			model = storyboardVideoRequestedModelName,
			model_family = (node.Params?.StoryboardVideoModelFamily ?? string.Empty),
			sub_model = (node.Params?.StoryboardVideoSubModel ?? string.Empty),
			prompt = prompt,
			images = new string[1] { referenceImage },
			quality = storyboardVideoQualityCode,
			mode = storyboardVideoYunWuMode,
			duration = storyboardVideoDuration,
			aspect_ratio = storyboardVideoAspectRatio,
			cfg_scale = 0.5,
			sound
		};
	}

	private static string GetYunWuVideoReferenceImage(string? referenceImagePath)
	{
		if (string.IsNullOrWhiteSpace(referenceImagePath))
		{
			return string.Empty;
		}
		if (Uri.TryCreate(referenceImagePath, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
		{
			return referenceImagePath;
		}
		return string.Empty;
	}

	private static int GetStoryboardVideoDuration(WorkflowNode node)
	{
		return Math.Clamp(node.Params?.StoryboardVideoDurationSeconds ?? 5, 1, 60);
	}

	private static string GetStoryboardVideoAspectRatio(WorkflowNode node)
	{
		string referenceImagePath = node.Params?.StoryboardVideoFusedImagePath ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(referenceImagePath) && File.Exists(referenceImagePath))
		{
			return GetImageAspectRatio(referenceImagePath, node.Params?.StoryboardVideoAspectRatio ?? "16:9");
		}

		return string.Equals(node.Params?.StoryboardVideoAspectRatio, "9:16", StringComparison.Ordinal) ? "9:16" : "16:9";
	}

	private static string GetStoryboardVideoQualityMode(WorkflowNode node)
	{
		string a = (node.Params?.StoryboardVideoQuality ?? string.Empty).Trim();
		if (string.Equals(a, "标准", StringComparison.OrdinalIgnoreCase))
		{
			return "std";
		}
		if (string.Equals(a, "超清", StringComparison.OrdinalIgnoreCase))
		{
			return "hd";
		}
		return "pro";
	}

	private static string GetStoryboardVideoQualityCode(WorkflowNode node)
	{
		return "standard";
	}

	private static string GetStoryboardVideoYunWuMode(WorkflowNode node)
	{
		return "std";
	}

	private static string GetStoryboardVideoRequestedModelName(WorkflowNode node, ModelInfo? credentialModel)
	{
		return WorkflowModelResolver.GetStoryboardVideoRequestedModelName(node, credentialModel);
	}

	private static Uri BuildStatusUri(string baseUrl, string taskId)
	{
		string[] array = new string[4]
		{
			"videos/generations/" + Uri.EscapeDataString(taskId),
			"video/generations/" + Uri.EscapeDataString(taskId),
			"tasks/" + Uri.EscapeDataString(taskId),
			"video/status/" + Uri.EscapeDataString(taskId)
		};
		return new Uri(new Uri(AppendTrailingSlash(baseUrl)), array[0]);
	}

	private static async Task<GeneratedArtifact> PollGenericVideoTaskAsync(ModelInfo model, WorkflowNode node, Uri statusUri, CancellationToken cancellationToken)
	{
		List<Uri> statusCandidates = new Uri[3]
		{
			statusUri,
			new Uri(statusUri.ToString().Replace("/videos/generations/", "/video/generations/", StringComparison.OrdinalIgnoreCase)),
			new Uri(statusUri.ToString().Replace("/videos/generations/", "/tasks/", StringComparison.OrdinalIgnoreCase))
		}.Distinct().ToList();
		for (int attempt = 0; attempt < 36; attempt++)
		{
			using (List<Uri>.Enumerator enumerator = statusCandidates.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					string videoUrl;
					string status;
					using (HttpRequestMessage request = new HttpRequestMessage(requestUri: enumerator.Current, method: HttpMethod.Get))
					{
						ApplyAuthorizationHeader(request, model);
						using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
						string json = await response.Content.ReadAsStringAsync(cancellationToken);
						if (!response.IsSuccessStatusCode)
						{
							continue;
						}
						using (JsonDocument jsonDocument = JsonDocument.Parse(json))
						{
							if (TryFindString(jsonDocument.RootElement, new string[3] { "video_url", "download_url", "url" }, out videoUrl))
							{
								return new GeneratedArtifact(await DownloadArtifactAsync(node, videoUrl, "mp4", cancellationToken), "已生成视频文件，可继续用于下游节点。");
							}
							if (TryFindString(jsonDocument.RootElement, new string[3] { "status", "state", "task_status" }, out status))
							{
								string normalized = status.Trim().ToLowerInvariant();
								bool flag;
								switch (normalized)
								{
								case "completed":
								case "succeeded":
								case "success":
								case "done":
									flag = true;
									break;
								default:
									flag = false;
									break;
								}
								if (flag)
								{
									if (TryFindString(jsonDocument.RootElement, new string[4] { "video_url", "download_url", "url", "output" }, out var finishedUrl))
									{
										return new GeneratedArtifact(await DownloadArtifactAsync(node, finishedUrl, "mp4", cancellationToken), "已生成视频文件，可继续用于下游节点。");
									}
									finishedUrl = null;
									finishedUrl = null;
								}
								switch (normalized)
								{
								case "failed":
								case "error":
								case "cancelled":
								case "canceled":
									flag = true;
									break;
								default:
									flag = false;
									break;
								}
								if (flag)
								{
									throw new InvalidOperationException("视频任务执行失败。");
								}
							}
						}
						goto IL_05dc;
					}
					IL_05dc:
					videoUrl = null;
					status = null;
					videoUrl = null;
					status = null;
				}
			}
			await Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken);
		}
		throw new InvalidOperationException("视频任务轮询超时。");
	}

	private static async Task<GeneratedArtifact> PollYunWuVideoTaskAsync(ModelInfo model, WorkflowNode node, Uri statusUri, CancellationToken cancellationToken)
	{
		string rootUrl = NormalizeYunWuRootUrl(model.Url);
		const int maxAttempts = 120;
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			string videoUrl;
			string status;
			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, statusUri))
			{
				ApplyAuthorizationHeader(request, model);
				using (HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken))
				{
					string json = await response.Content.ReadAsStringAsync(cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						using (JsonDocument jsonDocument = JsonDocument.Parse(json))
						{
							if (TryFindYunWuVideoUrl(jsonDocument.RootElement, out videoUrl))
							{
								string artifactPath = await DownloadArtifactAsync(node, videoUrl, "mp4", cancellationToken);
								ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), $"云调用成功，查询接口已返回最终视频：{Path.GetFileName(artifactPath)}");
								node.Params ??= new WorkflowNodeParameters();
								node.Params.StoryboardVideoLastError = string.Empty;
								return new GeneratedArtifact(artifactPath, "已通过云雾 API 生成视频文件，可继续用于下游节点。");
							}
							if (TryFindString(jsonDocument.RootElement, new string[3] { "task_status", "status", "state" }, out status))
							{
								string normalized = status.Trim().ToLowerInvariant();
								bool flag;
								switch (normalized)
								{
								case "succeed":
								case "succeeded":
								case "success":
								case "completed":
								case "done":
									flag = true;
									break;
								default:
									flag = false;
									break;
								}
								if (flag)
								{
									if (TryFindYunWuVideoUrl(jsonDocument.RootElement, out var finishedUrl))
									{
										string artifactPath2 = await DownloadArtifactAsync(node, finishedUrl, "mp4", cancellationToken);
										ModelCallLogService.LogSuccess(node.Type, model, ModelCallUsage.FromJson(jsonDocument.RootElement), $"云调用成功，任务状态查询已返回最终视频：{Path.GetFileName(artifactPath2)}");
										node.Params ??= new WorkflowNodeParameters();
										node.Params.StoryboardVideoLastError = string.Empty;
										return new GeneratedArtifact(artifactPath2, "已通过云雾 API 生成视频文件，可继续用于下游节点。");
									}
									GeneratedArtifact? generatedArtifact = await TryResumeYunWuVideoTaskAsync(model, node, rootUrl, cancellationToken);
									if (generatedArtifact.HasValue)
									{
										node.Params ??= new WorkflowNodeParameters();
										node.Params.StoryboardVideoLastError = string.Empty;
										return generatedArtifact.Value;
									}
									finishedUrl = null;
									finishedUrl = null;
								}
								switch (normalized)
								{
								case "failed":
								case "error":
								case "cancelled":
								case "canceled":
									flag = true;
									break;
								default:
									flag = false;
									break;
								}
								if (flag)
								{
									node.Params ??= new WorkflowNodeParameters();
									string taskId = node.Params.StoryboardVideoTaskId.OrDefault("未记录");
									node.Params.StoryboardVideoLastError = $"云雾视频任务执行失败。任务ID：{taskId}";
									throw new InvalidOperationException($"云雾视频任务执行失败，云端当前状态为“失败”，无法回收视频。任务ID：{taskId}");
								}
							}
							if ((attempt + 1) % 6 == 0)
							{
								GeneratedArtifact? checkpointArtifact = await TryResumeYunWuVideoTaskAsync(model, node, rootUrl, cancellationToken);
								if (checkpointArtifact.HasValue)
								{
									node.Params ??= new WorkflowNodeParameters();
									node.Params.StoryboardVideoLastError = string.Empty;
									return checkpointArtifact.Value;
								}
							}
							await Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken);
						}
						goto end_IL_0179;
					}
					await Task.Delay(TimeSpan.FromSeconds(5.0), cancellationToken);
					goto end_IL_0048;
					end_IL_0179:;
				}
				goto IL_066a;
				end_IL_0048:;
			}
				continue;
			IL_066a:
			videoUrl = null;
			status = null;
		}
		GeneratedArtifact? recoveredArtifact = await TryResumeYunWuVideoTaskAsync(model, node, rootUrl, cancellationToken);
		if (recoveredArtifact.HasValue)
		{
			node.Params ??= new WorkflowNodeParameters();
			node.Params.StoryboardVideoLastError = string.Empty;
			return recoveredArtifact.Value;
		}
		node.Params ??= new WorkflowNodeParameters();
		string timeoutTaskId = node.Params.StoryboardVideoTaskId.OrDefault("未记录");
		node.Params.StoryboardVideoLastError = $"云雾视频任务轮询超时。任务ID：{timeoutTaskId}，查询地址：{statusUri}";
		ModelCallLogService.LogFailure(node.Type, model, "云雾视频任务轮询超时。", null, $"任务ID：{timeoutTaskId}；查询地址：{statusUri}");
		throw new InvalidOperationException($"云雾视频任务轮询超时。任务ID：{timeoutTaskId}");
	}

	private static bool TryFindYunWuVideoUrl(JsonElement element, out string value)
	{
		List<string> urls = EnumerateYunWuVideoUrls(element);
		if (urls.Count > 0)
		{
			value = urls[0];
			return true;
		}

		value = string.Empty;
		return false;
	}

	private static List<string> EnumerateYunWuVideoUrls(JsonElement element)
	{
		var urls = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		static void AddCandidate(List<string> target, HashSet<string> seenSet, string? candidate)
		{
			if (string.IsNullOrWhiteSpace(candidate) || !LooksLikeVideoUrl(candidate))
			{
				return;
			}

			if (seenSet.Add(candidate))
			{
				target.Add(candidate);
			}
		}

		static void CollectPreferred(JsonElement root, List<string> target, HashSet<string> seenSet)
		{
			if (root.ValueKind != JsonValueKind.Object)
			{
				return;
			}

			if (root.TryGetProperty("detail", out JsonElement detail) && detail.ValueKind == JsonValueKind.Object)
			{
				if (TryFindString(detail, new[] { "video_url", "download_url", "videoUrl", "result_url", "file_url", "media_url", "play_url", "url" }, out string detailUrl))
				{
					AddCandidate(target, seenSet, detailUrl);
				}
			}

			if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
			{
				if (TryFindString(data, new[] { "video_url", "download_url", "videoUrl", "result_url", "file_url", "media_url", "play_url", "url" }, out string dataUrl))
				{
					AddCandidate(target, seenSet, dataUrl);
				}
			}
		}

		CollectPreferred(element, urls, seen);

		if (TryFindString(element, new[] { "video_url", "download_url", "videoUrl", "result_url", "file_url", "media_url", "play_url" }, out string primaryUrl))
		{
			AddCandidate(urls, seen, primaryUrl);
		}

		if (TryFindString(element, new[] { "url", "output" }, out string secondaryUrl))
		{
			AddCandidate(urls, seen, secondaryUrl);
		}

		return urls;
	}

	private static bool LooksLikeVideoUrl(string? candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate))
		{
			return false;
		}
		if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		if (!string.Equals(result.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(result.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string text = result.AbsolutePath.ToLowerInvariant();
		return text.EndsWith(".mp4", StringComparison.Ordinal)
			|| text.EndsWith(".mov", StringComparison.Ordinal)
			|| text.EndsWith(".webm", StringComparison.Ordinal)
			|| text.Contains("/file_download/", StringComparison.Ordinal)
			|| text.Contains("/videos/", StringComparison.Ordinal)
			|| text.Contains("/video/", StringComparison.Ordinal)
			|| !string.IsNullOrWhiteSpace(result.Host);
	}

	private static bool LooksLikeBinaryVideoContentType(string? mediaType)
	{
		if (string.IsNullOrWhiteSpace(mediaType))
		{
			return false;
		}
		return mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase);
	}

	private static string InferVideoExtension(string? mediaType, Uri sourceUri)
	{
		string text = mediaType?.Trim().ToLowerInvariant() ?? string.Empty;
		if (text == "video/webm")
		{
			return "webm";
		}
		if (text == "video/quicktime")
		{
			return "mov";
		}
		string path = sourceUri.AbsolutePath.ToLowerInvariant();
		if (path.EndsWith(".mov", StringComparison.Ordinal))
		{
			return "mov";
		}
		if (path.EndsWith(".webm", StringComparison.Ordinal))
		{
			return "webm";
		}
		return "mp4";
	}

	private static IEnumerable<Uri> BuildYunWuVideoQueryCandidates(string rootUrl, string? taskId, string? existingQueryUrl)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(existingQueryUrl) &&
			Uri.TryCreate(existingQueryUrl, UriKind.Absolute, out Uri uri) &&
			(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
			 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
			seen.Add(uri.AbsoluteUri))
		{
			yield return uri;
		}
		if (string.IsNullOrWhiteSpace(taskId))
		{
			yield break;
		}
		Uri baseUri = new Uri(AppendTrailingSlash(rootUrl));
		string escapedTaskId = Uri.EscapeDataString(taskId.Trim());
		string[] candidates = new string[3]
		{
			"v1/video/query?id=" + escapedTaskId,
			"v1/videos/" + escapedTaskId,
			"v1/videos/" + escapedTaskId + "/content"
		};
		foreach (string candidate in candidates)
		{
			Uri candidateUri = new Uri(baseUri, candidate);
			if (seen.Add(candidateUri.AbsoluteUri))
			{
				yield return candidateUri;
			}
		}
	}

	private static ModelInfo? ResolveExecutionModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
	{
		return WorkflowModelResolver.ResolveExecutionModel(settings, node, category);
	}

	private static ModelInfo? ResolveDirectStudioPromptTextModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveDirectStudioPromptTextModel(settings, node);
	}

	private static ModelInfo? ResolveDirectStudioImageExecutionModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveDirectStudioImageExecutionModel(settings, node);
	}

	private static ModelInfo? ResolveDirectStudioVideoExecutionModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveDirectStudioVideoExecutionModel(settings, node);
	}

	private static ModelInfo? ResolveStoryboardVideoExecutionModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveStoryboardVideoExecutionModel(settings, node);
	}

	private static ModelInfo? BuildRelayVideoExecutionModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.BuildRelayVideoExecutionModel(settings, node);
	}

	private static ModelInfo? ResolveSelectedModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
	{
		return WorkflowModelResolver.ResolveSelectedModel(settings, node, category);
	}

	private static ModelInfo? ResolveCharacterTextToImageModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveCharacterTextToImageModel(settings, node);
	}

	private static ModelInfo? ResolveCharacterImageToImageModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveCharacterImageToImageModel(settings, node);
	}

	private static ModelInfo? ResolveStoryboardTextToImageModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveStoryboardTextToImageModel(settings, node);
	}

	private static ModelInfo? ResolveStoryboardImageToImageModel(ModelSettings settings, WorkflowNode node)
	{
		return WorkflowModelResolver.ResolveStoryboardImageToImageModel(settings, node);
	}

	private static ModelInfo? ResolvePreferredOrDefaultNodeModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
	{
		return WorkflowModelResolver.ResolvePreferredOrDefaultNodeModel(settings, node, category);
	}

	private static ModelInfo? ResolveGlobalSelectedModel(ModelSettings settings, WorkflowNode node, ModelCategory category)
	{
		return WorkflowModelResolver.ResolveGlobalSelectedModel(settings, node, category);
	}

	private static void ApplyAuthorizationHeader(HttpRequestMessage request, ModelInfo model)
	{
		if (string.IsNullOrWhiteSpace(model.Key))
		{
			return;
		}
		if (ModelConfig.IsGeminiModelUrl(model.Url))
		{
			request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", model.Key);
			return;
		}
		if (!IsOllamaLike(model.Url) || !string.Equals(model.Key, "ollama", StringComparison.OrdinalIgnoreCase))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.Key);
		}
	}

	private static IEnumerable<string> GetApiBaseUrlCandidates(ModelInfo model)
	{
		string configured = NormalizeApiBaseUrl(model.Url);
		yield return configured;
		if (!IsOllamaLike(model.Url))
		{
			yield break;
		}
		string[] array = new string[2] { "http://127.0.0.1:11434/v1", "http://localhost:11434/v1" };
		string[] array2 = array;
		foreach (string candidate in array2)
		{
			if (!string.Equals(candidate, configured, StringComparison.OrdinalIgnoreCase))
			{
				yield return candidate;
			}
		}
	}

	private static bool IsOllamaLike(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		string text = url.ToLowerInvariant();
		return text.Contains("ollama") || result.Port == 11434;
	}

	private static bool IsYunWuLike(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		return IsYunWuHost(result.Host);
	}

	private static bool IsYunWuGeminiImageModel(string? modelId)
	{
		return !string.IsNullOrWhiteSpace(modelId) && (modelId.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) || modelId.StartsWith("imagen-", StringComparison.OrdinalIgnoreCase) || modelId.StartsWith("learnlm-", StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsStableDiffusionLike(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		string text = url.ToLowerInvariant();
		return text.Contains("sdapi") || result.Port == 7860;
	}

	private static bool IsComfyUiLike(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		string text = url.ToLowerInvariant();
		string text2 = result.AbsolutePath.ToLowerInvariant();
		return text.Contains("comfy") || result.Port == 8000 || result.Port == 8188 || text2.Contains("/object_info") || text2.Contains("/prompt") || text2.Contains("/queue") || text2.Contains("/history") || text2.Contains("/view");
	}

	private static bool SupportsConfiguredImageEdit(ModelInfo model)
	{
		if (model == null || string.IsNullOrWhiteSpace(model.Url) || IsYunWuGeminiImageModel(model.Id))
		{
			return false;
		}

		return IsGptImageModel(model) || HasExplicitImageEditEndpoint(model.Url);
	}

	private static string NormalizeApiBaseUrl(string rawUrl)
	{
		string text = (rawUrl ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("模型地址为空。");
		}
		text = text.TrimEnd('/');
		string value = "/models/";
		int num = text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		else if (text.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text;
			int length = "/models".Length;
			text = text2.Substring(0, text2.Length - length);
		}
		int num2 = text.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
		if (num2 >= 0)
		{
			return text.Substring(0, num2 + 3);
		}
		return text + "/v1";
	}

	private static Uri ResolveOpenAiImageEndpoint(string rawUrl, bool useEditEndpoint)
	{
		string configured = (rawUrl ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(configured))
		{
			throw new InvalidOperationException("图片模型地址为空。");
		}

		if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri configuredUri))
		{
			throw new InvalidOperationException("图片模型地址无效。");
		}

		string absolutePathUrl = configuredUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
		if (TryStripExplicitImageEndpoint(absolutePathUrl, out string endpointBaseUrl))
		{
			return new Uri(new Uri(AppendTrailingSlash(endpointBaseUrl)), useEditEndpoint ? "images/edits" : "images/generations");
		}

		string normalizedBaseUrl = NormalizeApiBaseUrl(configured);
		return new Uri(new Uri(AppendTrailingSlash(normalizedBaseUrl)), useEditEndpoint ? "images/edits" : "images/generations");
	}

	private static bool HasExplicitImageEditEndpoint(string rawUrl)
	{
		if (!Uri.TryCreate((rawUrl ?? string.Empty).Trim(), UriKind.Absolute, out Uri configuredUri))
		{
			return false;
		}

		string absolutePathUrl = configuredUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
		return absolutePathUrl.EndsWith("/images/edits", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryStripExplicitImageEndpoint(string absolutePathUrl, out string baseUrl)
	{
		string[] suffixes = new string[2] { "/images/generations", "/images/edits" };
		foreach (string suffix in suffixes)
		{
			if (absolutePathUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = absolutePathUrl.Substring(0, absolutePathUrl.Length - suffix.Length).TrimEnd('/');
				return !string.IsNullOrWhiteSpace(baseUrl);
			}
		}

		baseUrl = string.Empty;
		return false;
	}

	private static string NormalizeYunWuRootUrl(string rawUrl)
	{
		if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri result))
		{
			throw new InvalidOperationException("云雾地址无效。");
		}
		return IsYunWuHost(result.Host)
			? ModelConfig.DefaultYunWuBaseUrl.TrimEnd('/')
			: result.GetLeftPart(UriPartial.Authority).TrimEnd('/');
	}

	private static bool IsYunWuHost(string? host)
	{
		if (string.IsNullOrWhiteSpace(host))
		{
			return false;
		}
		return host.EndsWith("yunwu.ai", StringComparison.OrdinalIgnoreCase) ||
			host.EndsWith("yunwu.cloud", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<HttpResponseMessage> SendYunWuRequestAsync(HttpRequestMessage request, string configuredUrl, CancellationToken cancellationToken)
	{
		try
		{
			return await HttpClient.SendAsync(request, cancellationToken);
		}
		catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			throw new InvalidOperationException(BuildYunWuTransportFailureMessage(configuredUrl, "请求超时"), ex);
		}
		catch (HttpRequestException ex)
		{
			throw new InvalidOperationException(BuildYunWuTransportFailureMessage(configuredUrl, ex.Message), ex);
		}
	}

	private static string BuildYunWuTransportFailureMessage(string configuredUrl, string detail)
	{
		string current = string.IsNullOrWhiteSpace(configuredUrl) ? "空" : configuredUrl.Trim();
		return $"云雾 API 请求发送失败：{detail}\r\n当前地址：{current}\r\n请在模型设置中使用当前 API Key 对应的 API 根地址，例如 {ModelConfig.DefaultYunWuBaseUrl}，不要填写官网首页 https://yunwu.ai/。";
	}

	private static string NormalizeYunWuOpenAiBaseUrl(string rawUrl)
	{
		return NormalizeYunWuRootUrl(rawUrl) + "/v1";
	}

	private static (string AspectRatio, string Resolution) ResolveYunWuGeminiImageConfig(WorkflowNode node, string? sizeOverride, string? moduleName)
	{
		if (node.Type == "分镜图片")
		{
			return (AspectRatio: (node.Params?.StoryboardPanelOrientation == "9:16") ? "9:16" : "16:9", Resolution: "2K");
		}
		if (!string.IsNullOrWhiteSpace(moduleName) && moduleName.Contains("九宫格", StringComparison.Ordinal))
		{
			return (AspectRatio: "1:1", Resolution: "1K");
		}
		if (!string.IsNullOrWhiteSpace(moduleName) && moduleName.Contains("三视图", StringComparison.Ordinal))
		{
			return (AspectRatio: "16:9", Resolution: "1K");
		}
		if (TryParseSize(sizeOverride, out var width, out var height))
		{
			if (width == height)
			{
				return (AspectRatio: "1:1", Resolution: "1K");
			}
			return (width > height) ? (AspectRatio: "16:9", Resolution: "1K") : (AspectRatio: "9:16", Resolution: "1K");
		}
		return (AspectRatio: "1:1", Resolution: "1K");
	}

	private static bool TryExtractInlineImage(JsonElement root, out string base64, out string mimeType)
	{
		if (root.ValueKind == JsonValueKind.Object)
		{
			if (root.TryGetProperty("inlineData", out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("data", out var value2) && value2.ValueKind == JsonValueKind.String)
			{
				base64 = value2.GetString() ?? string.Empty;
				mimeType = ((value.TryGetProperty("mimeType", out var value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "image/png") : "image/png");
				return !string.IsNullOrWhiteSpace(base64);
			}
			foreach (JsonProperty item in root.EnumerateObject())
			{
				if (TryExtractInlineImage(item.Value, out base64, out mimeType))
				{
					return true;
				}
			}
		}
		else if (root.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item2 in root.EnumerateArray())
			{
				if (TryExtractInlineImage(item2, out base64, out mimeType))
				{
					return true;
				}
			}
		}
		base64 = string.Empty;
		mimeType = "image/png";
		return false;
	}

	private static (string Data, string MimeType) ReadInlineImageData(string filePath)
	{
		string text = ConvertFileToDataUrl(filePath);
		int num = text.IndexOf(',');
		if (num < 0)
		{
			throw new InvalidOperationException("无法读取参考图数据：" + filePath);
		}
		string text2 = text.Substring(0, num);
		object obj;
		string text3;
		if (!text2.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			obj = "image/png";
		}
		else
		{
			text3 = text2;
			obj = text3.Substring(5, text3.Length - 5).Split(';')[0];
		}
		string item = (string)obj;
		text3 = text;
		int num2 = num + 1;
		string item2 = text3.Substring(num2, text3.Length - num2);
		return (Data: item2, MimeType: item);
	}

	private static string MimeTypeToExtension(string? mimeType)
	{
		string text = mimeType?.ToLowerInvariant();
		bool flag = false;
		string result = ((text == "image/jpeg") ? "jpg" : ((!(text == "image/webp")) ? "png" : "webp"));
		bool flag2 = false;
		return result;
	}

	private static bool TryParseSize(string? value, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string[] array = value.Split('x', 'X');
		return array.Length == 2 && int.TryParse(array[0], out width) && int.TryParse(array[1], out height);
	}

	private static IEnumerable<Uri> BuildYunWuVideoSubmitEndpoints(string rawUrl, bool hasReferenceImage)
	{
		string root = NormalizeYunWuRootUrl(rawUrl);
		string explicitPath = string.Empty;
		if (Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri uri))
		{
			explicitPath = uri.AbsolutePath.Trim();
		}
		List<string> candidates = new List<string>();
		if (!string.IsNullOrWhiteSpace(explicitPath) && !string.Equals(explicitPath, "/", StringComparison.Ordinal))
		{
			candidates.Add(explicitPath.TrimStart('/'));
		}
		candidates.Add("v1/video/create");
		using IEnumerator<string> enumerator = candidates.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct<string>(StringComparer.OrdinalIgnoreCase).GetEnumerator();
		while (enumerator.MoveNext())
		{
			yield return new Uri(relativeUri: enumerator.Current.TrimStart('/'), baseUri: new Uri(AppendTrailingSlash(root)));
		}
	}

	private static Uri BuildYunWuVideoStatusUri(string rootUrl, string submitPath, string taskId)
	{
		string text = (submitPath ?? string.Empty).TrimEnd('/');
		string relativeUri = (text.EndsWith("/image2video", StringComparison.OrdinalIgnoreCase) ? (text + "/" + Uri.EscapeDataString(taskId)) : ((!text.EndsWith("/text2video", StringComparison.OrdinalIgnoreCase)) ? ("v1/video/query?id=" + Uri.EscapeDataString(taskId)) : (text + "/" + Uri.EscapeDataString(taskId)))).TrimStart('/');
		return new Uri(new Uri(AppendTrailingSlash(rootUrl)), relativeUri);
	}

	private static string NormalizeStableDiffusionBaseUrl(string rawUrl)
	{
		string text = (rawUrl ?? string.Empty).Trim().TrimEnd('/');
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("图片模型地址为空。");
		}
		if (text.EndsWith("/sdapi/v1", StringComparison.OrdinalIgnoreCase))
		{
			return text + "/";
		}
		return text + "/sdapi/v1/";
	}

	private static string NormalizeComfyUiBaseUrl(string rawUrl)
	{
		string text = (rawUrl ?? string.Empty).Trim().TrimEnd('/');
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("ComfyUI 地址为空。");
		}
		string[] array = new string[5] { "/prompt", "/queue", "/history", "/view", "/object_info" };
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			if (text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text;
				int length = text2.Length;
				text = text3.Substring(0, text3.Length - length);
				break;
			}
		}
		return text;
	}

	private static string AppendTrailingSlash(string value)
	{
		return value.EndsWith("/", StringComparison.Ordinal) ? value : (value + "/");
	}

	private static bool TryReadChatCompletionContent(JsonElement root, out string content)
	{
		content = string.Empty;
		if (!root.TryGetProperty("choices", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			return false;
		}
		JsonElement jsonElement = value[0];
		if (jsonElement.TryGetProperty("message", out var value2) && value2.TryGetProperty("content", out var value3))
		{
			content = ReadContentValue(value3);
			return !string.IsNullOrWhiteSpace(content);
		}
		if (jsonElement.TryGetProperty("text", out var value4) && value4.ValueKind == JsonValueKind.String)
		{
			content = value4.GetString() ?? string.Empty;
			return !string.IsNullOrWhiteSpace(content);
		}
		return false;
	}

	private static string ReadContentValue(JsonElement value)
	{
		if (value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? string.Empty;
		}
		if (value.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (JsonElement item in value.EnumerateArray())
		{
			JsonElement value2;
			if (item.ValueKind == JsonValueKind.String)
			{
				stringBuilder.AppendLine(item.GetString());
			}
			else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out value2) && value2.ValueKind == JsonValueKind.String)
			{
				stringBuilder.AppendLine(value2.GetString());
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private static bool TryFindTextCompletionFallbackContent(JsonElement root, out string content)
	{
		content = string.Empty;
		if (TryReadStringProperty(root, "output_text", out content) ||
			TryReadStringProperty(root, "response", out content) ||
			TryReadStringProperty(root, "result", out content))
		{
			return IsPlausibleTextCompletionContent(content);
		}

		if (root.TryGetProperty("output", out var outputElement) &&
			TryReadOutputArrayText(outputElement, out content))
		{
			return true;
		}

		if (root.TryGetProperty("data", out var dataElement))
		{
			if (TryReadStringProperty(dataElement, "output_text", out content) ||
				TryReadStringProperty(dataElement, "response", out content) ||
				TryReadStringProperty(dataElement, "result", out content) ||
				TryReadOutputArrayText(dataElement, out content))
			{
				return IsPlausibleTextCompletionContent(content);
			}
		}

		return false;
	}

	private static bool TryReadStringProperty(JsonElement element, string propertyName, out string value)
	{
		value = string.Empty;
		if (element.ValueKind != JsonValueKind.Object ||
			!element.TryGetProperty(propertyName, out var property) ||
			property.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		value = property.GetString()?.Trim() ?? string.Empty;
		return IsPlausibleTextCompletionContent(value);
	}

	private static bool TryReadOutputArrayText(JsonElement element, out string value)
	{
		value = string.Empty;
		if (element.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		StringBuilder builder = new StringBuilder();
		foreach (JsonElement item in element.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				builder.AppendLine(item.GetString());
				continue;
			}

			if (item.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (item.TryGetProperty("text", out var textElement))
			{
				builder.AppendLine(ReadContentValue(textElement));
			}
			else if (item.TryGetProperty("content", out var contentElement))
			{
				builder.AppendLine(ReadContentValue(contentElement));
			}
		}

		value = builder.ToString().Trim();
		return IsPlausibleTextCompletionContent(value);
	}

	private static bool IsPlausibleTextCompletionContent(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		string text = value.Trim();
		if (Regex.IsMatch(text, @"^(chat)?cmpl-[A-Za-z0-9_-]+$", RegexOptions.IgnoreCase) ||
			Regex.IsMatch(text, @"^(msg|resp|req|run|task)_[A-Za-z0-9_-]+$", RegexOptions.IgnoreCase))
		{
			return false;
		}

		return text.Length >= 2;
	}

	private static bool TryFindString(JsonElement element, IEnumerable<string> targetNames, out string value)
	{
		StringComparer comparer = StringComparer.OrdinalIgnoreCase;
		string[] array = targetNames.ToArray();
		if (element.ValueKind == JsonValueKind.String)
		{
			value = element.GetString() ?? string.Empty;
			return !string.IsNullOrWhiteSpace(value);
		}
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (array.Any((string name) => comparer.Equals(name, property.Name)) && property.Value.ValueKind == JsonValueKind.String)
				{
					value = property.Value.GetString() ?? string.Empty;
					if (!string.IsNullOrWhiteSpace(value))
					{
						return true;
					}
				}
				if (TryFindString(property.Value, array, out value))
				{
					return true;
				}
			}
		}
		if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				if (TryFindString(item, array, out value))
				{
					return true;
				}
			}
		}
		value = string.Empty;
		return false;
	}

	private static string EnsureOutputDirectory(string nodeType)
	{
		string text = string.Concat((nodeType ?? "node").Where((char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'));
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "node";
		}
		string text2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "outputs", text);
		Directory.CreateDirectory(text2);
		return text2;
	}

	private static string SaveBase64Artifact(WorkflowNode node, string rawBase64, string extension)
	{
		string text = rawBase64;
		int num = text.IndexOf(',');
		if (num >= 0)
		{
			string text2 = text;
			int num2 = num + 1;
			text = text2.Substring(num2, text2.Length - num2);
		}
		byte[] bytes = Convert.FromBase64String(text);
		string path = EnsureOutputDirectory(node.Type);
		string text3 = Path.Combine(path, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.{extension}");
		File.WriteAllBytes(text3, bytes);
		return text3;
	}

	private static async Task<string> DownloadArtifactAsync(WorkflowNode node, string url, string extension, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken);
		response.EnsureSuccessStatusCode();
		byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		string outputDirectory = EnsureOutputDirectory(node.Type);
		string filePath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
		await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
		return filePath;
	}

	private static async Task<string> SaveDownloadedArtifactAsync(WorkflowNode node, byte[] bytes, string extension, CancellationToken cancellationToken)
	{
		string outputDirectory = EnsureOutputDirectory(node.Type);
		string filePath = Path.Combine(outputDirectory, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
		await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
		return filePath;
	}

	private static GeneratedArtifact RelocateStoryboardVideoArtifact(WorkflowDocument document, WorkflowNode node, GeneratedArtifact artifact, int sequence = 0)
	{
		if (!string.Equals(node.Type, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase))
		{
			return artifact;
		}
		if (string.IsNullOrWhiteSpace(artifact.Path) || !File.Exists(artifact.Path))
		{
			return artifact;
		}
		string projectRootPath = ProjectStoragePaths.EnsureProjectRootPath(ProjectWorkspaceMode.AiAnimeProject, document.ProjectName);
		string targetDirectory = Path.Combine(projectRootPath, "fenjing", $"{ProjectStoragePaths.SanitizeFileSegment(node.Id)}_{ProjectStoragePaths.SanitizeFileSegment(node.Type)}");
		Directory.CreateDirectory(targetDirectory);
		string extension = Path.GetExtension(artifact.Path);
		string sequencePart = sequence > 0 ? $"_shot{sequence:000}" : string.Empty;
		string targetPath = Path.Combine(targetDirectory, $"{ProjectStoragePaths.SanitizeFileSegment(node.Id)}{sequencePart}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{extension}");
		if (!string.Equals(Path.GetFullPath(artifact.Path), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(artifact.Path, targetPath, true);
		}
		return new GeneratedArtifact(targetPath, artifact.Description);
	}

	private static string ConvertFileToDataUrl(string filePath)
	{
		string text = Path.GetExtension(filePath).ToLowerInvariant();
		bool flag = false;
		string text2;
		switch (text)
		{
		case ".png":
			text2 = "image/png";
			break;
		case ".jpg":
		case ".jpeg":
			text2 = "image/jpeg";
			break;
		case ".webp":
			text2 = "image/webp";
			break;
		default:
			text2 = "application/octet-stream";
			break;
		}
		bool flag2 = false;
		string text3 = text2;
		string text4 = Convert.ToBase64String(File.ReadAllBytes(filePath));
		return "data:" + text3 + ";base64," + text4;
	}

	private static string ConvertFileToYunWuImagePayload(string filePath)
	{
		return Convert.ToBase64String(File.ReadAllBytes(filePath));
	}

	private static bool ExecuteSimulatedImageNode(WorkflowDocument document, WorkflowNode node, string input, string title, string summary, Color accentColor, string reason)
	{
		string title2 = ((node.Type == "角色设计") ? "角色视图模拟图" : "分镜图片模拟图");
		string text = ((node.Type == "角色设计") ? BuildStructuredCharacterViewSummary(document, node) : BuildStructuredStoryboardImageSummary(document, node));
		summary = text;
		string artifactPath = SaveSimulatedImage(node, title2, text + Environment.NewLine + reason, accentColor);
		WorkflowExecutor.ApplyArtifactResult(node, input, artifactPath, "image", "已生成模拟图片产物，可继续作为下游参考。" + Environment.NewLine + summary);
		return true;
	}

	private static bool ExecuteSimulatedVideoNode(WorkflowNode node, string input, string reason)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("模拟视频任务");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder2);
		handler.AppendLiteral("节点：");
		handler.AppendFormatted(node.Type);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("生成时间：");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("镜头设计：");
		stringBuilder.AppendLine("1. 开场镜头：建立人物与环境关系，3 秒内给出核心冲突。");
		stringBuilder.AppendLine("2. 推进镜头：使用近景和跟拍强化情绪，加入动作变化。");
		stringBuilder.AppendLine("3. 结尾镜头：停留在悬念信息或人物反应上，形成转场钩子。");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("视频提示词：");
		stringBuilder.AppendLine(WorkflowExecutor.BuildVideoPrompt(node, input));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("说明：");
		stringBuilder.AppendLine(reason);
		string artifactPath = SaveSimulatedTextArtifact(node, "txt", stringBuilder.ToString(), "storyboard");
		WorkflowExecutor.ApplyArtifactResult(node, input, artifactPath, "video", "已生成模拟视频任务文件，包含镜头节奏和视频提示词，可继续用于视频合集节点。");
		return true;
	}

	private static bool ExecuteSimulatedCollectionNode(WorkflowDocument document, WorkflowNode node, string input, string reason)
	{
		List<WorkflowNode> list = (from candidate in WorkflowExecutor.CollectUpstreamNodes(document, node)
			where !string.IsNullOrWhiteSpace(candidate.ArtifactPath)
			select candidate).ToList();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("模拟视频合集清单");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder2);
		handler.AppendLiteral("节点：");
		handler.AppendFormatted(node.Type);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("生成时间：");
		handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine();
		if (list.Count == 0)
		{
			stringBuilder.AppendLine("当前没有检测到可合并的视频文件，已生成流程说明型合集结果。");
			stringBuilder.AppendLine(reason);
		}
		else
		{
			stringBuilder.AppendLine("建议合并顺序：");
			foreach (var item in list.Select((WorkflowNode value, int index) => (value: value, index: index)))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder5 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(6, 3, stringBuilder2);
				handler.AppendFormatted(item.index + 1);
				handler.AppendLiteral(". ");
				handler.AppendFormatted(item.value.Type);
				handler.AppendLiteral(" -> ");
				handler.AppendFormatted(item.value.ArtifactPath);
				stringBuilder5.AppendLine(ref handler);
			}
		}
		string artifactPath = SaveSimulatedTextArtifact(node, "txt", stringBuilder.ToString(), "collection");
		WorkflowExecutor.ApplyArtifactResult(node, input, artifactPath, "collection", "已生成模拟合集结果，可继续作为后续导出和检查依据。");
		return true;
	}

	private static IEnumerable<string> BuildEpisodePlans(int episodes, string coreIdea)
	{
		foreach (int episodeIndex in Enumerable.Range(1, Math.Min(episodes, 12)))
		{
			if (1 == 0)
			{
			}
			string text = episodeIndex switch
			{
				1 => "人物登场，冲突种子埋下", 
				2 => "误会升级，关系被迫绑定", 
				3 => "利益冲突爆发，情绪升温", 
				4 => "第一次合作，表面缓和", 
				5 => "真相露出一角，出现新对手", 
				6 => "关系反噬，信任破裂", 
				7 => "身份或过去被揭穿", 
				8 => "情绪低谷，逼近决裂", 
				9 => "关键证据出现，局势反转", 
				10 => "正面对抗，情感摊牌", 
				11 => "终局布局，反派失控", 
				_ => "收束主线，完成情感与命运落点", 
			};
			if (1 == 0)
			{
			}
			string text2 = text;
			string beat = text2;
			yield return $"第 {episodeIndex} 集：围绕“{TrimForSummary(coreIdea, 24)}”推进，重点是{beat}。";
		}
	}

	private static string GuessDramaTitle(string? seed, string? genre)
	{
		string text = (seed ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = genre ?? "命运反转";
		}
		text = text.Replace("。", string.Empty).Replace("，", string.Empty);
		if (text.Length > 10)
		{
			text = text.Substring(0, 10);
		}
		return text;
	}

	private static string TrimForSummary(string? value, int maxLength)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = value.Replace(Environment.NewLine, " ").Replace('\r', ' ').Replace('\n', ' ')
			.Trim();
		return (text.Length <= maxLength) ? text : (text.Substring(0, maxLength) + "...");
	}

	private static string SaveSimulatedTextArtifact(WorkflowNode node, string extension, string content, string suffix)
	{
		string path = EnsureOutputDirectory(node.Type);
		string text = Path.Combine(path, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}.{extension}");
		File.WriteAllText(text, content, Encoding.UTF8);
		return text;
	}

	private static string SaveSimulatedImage(WorkflowNode node, string title, string body, Color accent)
	{
		var (num, num2) = GetImageCanvasSize(node);
		using Bitmap bitmap = new Bitmap(num, num2);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.Clear(Color.FromArgb(20, 22, 28));
		using SolidBrush brush = new SolidBrush(accent);
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(34, 38, 46));
		using SolidBrush brush3 = new SolidBrush(Color.WhiteSmoke);
		using SolidBrush brush4 = new SolidBrush(Color.FromArgb(188, 198, 214));
		using Pen pen = new Pen(Color.FromArgb(88, accent), 3f);
		using Font font = new Font("Microsoft YaHei", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
		using Font font2 = new Font("Microsoft YaHei", 18f, FontStyle.Regular, GraphicsUnit.Pixel);
		using Font font3 = new Font("Microsoft YaHei", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
		graphics.FillRectangle(brush, new Rectangle(0, 0, num, 18));
		graphics.FillRectangle(brush2, new Rectangle(56, 72, num - 112, num2 - 144));
		graphics.DrawRectangle(pen, new Rectangle(56, 72, num - 112, num2 - 144));
		graphics.DrawString(title, font, brush3, new RectangleF(92f, 120f, num - 184, 50f));
		graphics.DrawString(body, font2, brush4, new RectangleF(92f, 210f, num - 184, num2 - 360));
		graphics.DrawString("JSAI 模拟产物", font3, brush, new RectangleF(92f, num2 - 120, num - 184, 30f));
		string path = EnsureOutputDirectory(node.Type);
		string text = Path.Combine(path, $"{node.Id}_{DateTime.Now:yyyyMMdd_HHmmss}_sim.png");
		bitmap.Save(text, ImageFormat.Png);
		return text;
	}
}
