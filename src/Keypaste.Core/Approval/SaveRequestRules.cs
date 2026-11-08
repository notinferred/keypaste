using Keypaste.Core.Agents;
using Keypaste.Core.Ipc;

namespace Keypaste.Core.Approval;

/// <summary>The shape of an agent's request to save a value, checked by the bridge before it asks and by the owner again.</summary>
public static class SaveRequestRules
{
    /// <summary>The field that names an item's password.</summary>
    public const string Password = "password";

    /// <summary>Why a request names no place a value can be saved, or null.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The argument and the rule it breaks, or null.</returns>
    public static CredentialRequestProblem? Check(SaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var item = request.Item.Length > 0 || request.Field.Length > 0;
        var project = request.Project.Length > 0 || request.Environment.Length > 0 || request.Key.Length > 0;

        if (item == project)
        {
            return new CredentialRequestProblem("item", "names an item and a field, or a project, an environment and a key, and not both");
        }

        if (project)
        {
            return request.Project.Length == 0 || request.Environment.Length == 0 || request.Key.Length == 0
                ? new CredentialRequestProblem("project", "goes with an environment and a key")
                : CredentialRequestRules.Check("project", "password", request.Reason) is { } reason ? reason : null;
        }

        if (CredentialRequestRules.Check(request.Item, Password, request.Reason) is { } problem)
        {
            return problem;
        }

        if (EntryHandle.Classify(request.Item) != EntryAddressKind.Path)
        {
            return new CredentialRequestProblem("item", "is the item's path, such as work/github");
        }

        return string.Equals(request.Field, Password, StringComparison.Ordinal)
            || (CredentialFields.IsAgentCustom(request.Field) && !FieldCategories.IsPasskey(request.Field))
                ? null
                : new CredentialRequestProblem("field", "is password or a custom field's name");
    }
}
