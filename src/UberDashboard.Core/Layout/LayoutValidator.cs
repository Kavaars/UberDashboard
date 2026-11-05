using UberDashboard.Core.Localization;
namespace UberDashboard.Core.Layout;

/// <summary>Pojedynczy problem znaleziony w dokumencie układu.</summary>
public sealed record LayoutValidationIssue(
    LayoutErrorCode Code,
    string Message,
    PanelInstanceId? InstanceId = null,
    Guid? SplitId = null);

/// <summary>Wynik walidacji układu.</summary>
public sealed record LayoutValidationResult(IReadOnlyList<LayoutValidationIssue> Issues)
{
    /// <summary>Układ zgodny ze wszystkimi niezmiennikami.</summary>
    public static LayoutValidationResult Valid { get; } = new([]);

    /// <summary><see langword="true"/>, gdy nie zgłoszono żadnego problemu.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Skrócony opis problemów do logu i komunikatu UI.</summary>
    public string Describe()
    {
        if (IsValid)
        {
            return UiText.Get("LayoutIsValid");
        }

        var codes = new List<string>(Issues.Count);
        foreach (var issue in Issues)
        {
            codes.Add(issue.Code.ToString());
        }

        return string.Join(", ", codes);
    }
}

/// <summary>
/// Waliduje niezmienniki drzewa układu. Sprawdzenie obecności instancji wykonuje
/// aplikacja, przekazując zbiór widocznych identyfikatorów z bazy.
/// </summary>
public static class LayoutValidator
{
    /// <summary>
    /// Sprawdza niepusty/nienienny stan, kształt węzłów, unikalność identyfikatorów,
    /// limity oraz zgodność liści ze zbiorem widocznych instancji.
    /// </summary>
    public static LayoutValidationResult Validate(
        LayoutTree tree,
        IReadOnlySet<PanelInstanceId> visibleInstances,
        LayoutLimits limits)
    {
        var issues = new List<LayoutValidationIssue>();

        if (tree.Root is null)
        {
            if (visibleInstances.Count > 0)
            {
                issues.Add(new LayoutValidationIssue(
                    LayoutErrorCode.MissingRoot,
                    UiText.Get("TheLayoutIsEmptyButHasVisible")));
            }

            return new LayoutValidationResult(issues);
        }

        if (visibleInstances.Count == 0)
        {
            issues.Add(new LayoutValidationIssue(
                LayoutErrorCode.InconsistentEmptyState,
                UiText.Get("TheLayoutHasLeavesButNoVisible")));
        }

        var seenInstances = new HashSet<PanelInstanceId>();
        var seenSplits = new HashSet<Guid>();
        var nodeCount = 0;
        var stack = new Stack<(LayoutNode Node, int Depth)>();
        stack.Push((tree.Root, 1));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            nodeCount++;

            if (nodeCount > limits.MaxNodes)
            {
                issues.Add(new LayoutValidationIssue(
                    LayoutErrorCode.NodeLimitExceeded,
                    UiText.Format("TheLayoutExceedsTheLimitOfNodes", limits.MaxNodes)));
                break;
            }

            if (depth > limits.MaxDepth)
            {
                issues.Add(new LayoutValidationIssue(
                    LayoutErrorCode.DepthLimitExceeded,
                    UiText.Format("TheLayoutExceedsTheDepthLimitOf", limits.MaxDepth)));
                break;
            }

            switch (node)
            {
                case LeafNode leaf:
                {
                    if (leaf.InstanceId.IsEmpty)
                    {
                        issues.Add(new LayoutValidationIssue(
                            LayoutErrorCode.UnknownInstance,
                            UiText.Get("ALeafHasAnEmptyInstanceId")));
                        break;
                    }

                    if (!seenInstances.Add(leaf.InstanceId))
                    {
                        issues.Add(new LayoutValidationIssue(
                            LayoutErrorCode.DuplicateInstance,
                            UiText.Format("InstanceAppearsMoreThanOnceInThe", leaf.InstanceId),
                            leaf.InstanceId));
                        break;
                    }

                    if (!visibleInstances.Contains(leaf.InstanceId))
                    {
                        issues.Add(new LayoutValidationIssue(
                            LayoutErrorCode.UnknownInstance,
                            UiText.Format("ALeafPointsToInstanceWhichIs", leaf.InstanceId),
                            leaf.InstanceId));
                    }

                    break;
                }

                case SplitNode split:
                {
                    if (split.SplitId == Guid.Empty)
                    {
                        issues.Add(new LayoutValidationIssue(
                            LayoutErrorCode.InvalidSplitId,
                            UiText.Get("ASplitHasAnEmptyId")));
                    }
                    else if (!seenSplits.Add(split.SplitId))
                    {
                        issues.Add(new LayoutValidationIssue(
                            LayoutErrorCode.InvalidSplitId,
                            UiText.Format("SplitIdAppearsMoreThanOnce", split.SplitId),
                            SplitId: split.SplitId));
                    }

                    if (!double.IsFinite(split.Ratio) || split.Ratio <= 0d || split.Ratio >= 1d)
                    {
                        issues.Add(new LayoutValidationIssue(
                            LayoutErrorCode.InvalidRatio,
                            UiText.Format("SplitHasAnInvalidRatio", split.SplitId, split.Ratio),
                            SplitId: split.SplitId));
                    }

                    stack.Push((split.Second, depth + 1));
                    stack.Push((split.First, depth + 1));
                    break;
                }

                default:
                    issues.Add(new LayoutValidationIssue(
                        LayoutErrorCode.MalformedDocument,
                        UiText.Get("TheLayoutContainsAnUnknownNodeType")));
                    break;
            }
        }

        foreach (var visible in visibleInstances)
        {
            if (!seenInstances.Contains(visible))
            {
                issues.Add(new LayoutValidationIssue(
                    LayoutErrorCode.UnknownInstance,
                    UiText.Format("VisibleInstanceIsMissingFromTheLayout", visible),
                    visible));
            }
        }

        return new LayoutValidationResult(issues);
    }
}
