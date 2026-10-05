using System.Text.Json;
using Karolina.Core;

namespace Karolina.Desktop;

public sealed record ChatInput(string Text, string Model, string Effort, string Access, string[] Documents, string? ThreadId, string? TaskId = null, string? PlanId = null, string Mode = "execute-task", Dictionary<string, string[]>? ResourceSelections = null,string? ExplanationReviewId=null,int? ExplanationRevision=null,string[]? GraphNodeIds=null);
public sealed record ReviewStartInput(string PlanId);
public sealed record ReviewSubmitInput(string Id, int Revision, string Summary);
public sealed record ReviewFileInput(string Id, int Revision, string Path, string Fingerprint, bool? Accept, ReviewNote[] Notes);
public sealed record ReviewTaskInput(string Id, int Revision, bool Accept, string Feedback);
public sealed record ApprovalInput(string Id, bool Accept);
public sealed record UnityInput(string Action, string? Assembly, string? Class, string? Mode);
public sealed record DocumentInput(string Type, string Title, string Domain, string[]? Requirements, string? EnglishTitle = null, string? EnglishDomain = null, string? RuleSection = null);
public sealed record DocumentSave(string Id, string Markdown, string ExpectedHash, string? ChangeSummary);
public sealed record DocumentDetails(string Id, string ExpectedMetadataHash, string? Status, string[]? Requirements, string? Conclusion);
public sealed record EditorInput(bool Dirty);

public sealed record DocumentIndexInput(string Id, string ExpectedMetadataHash, string[] Tags, PlannedChange[]? PlannedChanges, ResourceReference[]? ResourceRefs);
public sealed record ReviewOriginInput(string Id, int Revision, string Path, string Fingerprint, string Origin, string Reason);
public sealed record ResourceRevealInput(string Path);
public sealed record ReviewExplainInput(string Id,int Revision,string Model,string Effort);
public sealed record ToolRegisterInput(ExtensionTool Definition,string Manual);
public sealed record ToolRunInput(string Id,string Hash,JsonElement Arguments,string? OperationId=null);
public sealed record ReviewExplanationStop(string Id, string RunId);
