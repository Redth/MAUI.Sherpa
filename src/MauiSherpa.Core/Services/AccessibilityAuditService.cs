using System.Globalization;
using MauiSherpa.Core.Models.DevFlow;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Runs accessibility audit rules against a visual tree fetched from a DevFlow agent.
/// </summary>
public class AccessibilityAuditService
{
    private static readonly string[] ImageTypes = { "Image", "ImageButton" };
    private static readonly string[] ButtonTypes = { "Button", "ImageButton", "SwipeItem" };
    private static readonly string[] InputTypes = { "Entry", "Editor", "SearchBar", "DatePicker", "TimePicker", "Picker", "Stepper", "Slider", "Switch", "CheckBox", "RadioButton" };
    private static readonly string[] InteractiveTypes = ButtonTypes.Concat(InputTypes).ToArray();
    private static readonly string[] TextElementTypes = { "Label", "Button", "Entry", "Editor", "SearchBar", "Span", "RadioButton" };

    private readonly List<IAccessibilityRule> _rules;

    public AccessibilityAuditService()
    {
        _rules = new List<IAccessibilityRule>
        {
            new MissingImageDescriptionRule(),
            new MissingButtonLabelRule(),
            new MissingInputLabelRule(),
            new TouchTargetTooSmallRule(),
            new DuplicateAutomationIdRule(),
            new NotFocusableInteractiveRule(),
            new MissingHeadingLevelRule(),
            new LowContrastRule(),
        };
    }

    // Properties needed per element type (only what can't be derived from tree or native a11y data)
    private static readonly string[] ColorFontProps = { "FontSize", "TextColor", "BackgroundColor", "Background" };
    private static readonly HashSet<string> TextElementSet = new(StringComparer.OrdinalIgnoreCase)
        { "Label", "Span", "Button", "ImageButton", "Entry", "Editor", "SearchBar", "RadioButton" };
    private static readonly HashSet<string> ImageElementSet = new(StringComparer.OrdinalIgnoreCase)
        { "Image", "ImageButton" };
    private static readonly HashSet<string> InteractiveElementSet = new(StringComparer.OrdinalIgnoreCase)
        { "Button", "ImageButton", "SwipeItem", "Entry", "Editor", "SearchBar", "DatePicker", "TimePicker",
          "Picker", "Stepper", "Slider", "Switch", "CheckBox", "RadioButton" };

    /// <summary>
    /// Run all audit rules against the visual tree, fetching additional properties as needed.
    /// Uses native accessibility data embedded in the tree (element.Accessibility) for labels,
    /// hints, and heading detection. Only fetches color/font/source properties via HTTP.
    /// </summary>
    public async Task<AccessibilityAuditResult> AuditAsync(
        List<DevFlowElementInfo> tree,
        DevFlowAgentClient client,
        CancellationToken ct = default)
    {
        var allElements = FlattenTree(tree);

        // Build native a11y lookup from tree data (agent populates element.Accessibility since v2)
        var nativeA11yLookup = new Dictionary<string, DevFlowAccessibilityElement>(StringComparer.Ordinal);
        foreach (var el in allElements.Where(e => e.Accessibility != null))
        {
            nativeA11yLookup[el.Id] = new DevFlowAccessibilityElement
            {
                Id = el.Id,
                Type = el.Type,
                AutomationId = el.AutomationId,
                Text = el.Text,
                WindowBounds = el.WindowBounds,
                Accessibility = el.Accessibility,
            };
        }

        // Fallback: if the connected agent is older and doesn't embed accessibility in the tree,
        // fetch the dedicated endpoint instead
        if (nativeA11yLookup.Count == 0)
        {
            try
            {
                using var a11yCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                a11yCts.CancelAfter(TimeSpan.FromSeconds(5));
                var a11yTree = await client.GetAccessibilityTreeAsync(ct: a11yCts.Token);
                if (a11yTree?.AccessibilityElements != null)
                    foreach (var el in a11yTree.AccessibilityElements)
                        nativeA11yLookup[el.Id] = el;
            }
            catch { /* older agent without /api/accessibility support */ }
        }

        // Only fetch properties that can't be derived from tree data or native a11y:
        //   - Source:                    images (to distinguish decorative/unloaded)
        //   - FontSize, TextColor,
        //     BackgroundColor, Background: text elements (for contrast + heading detection)
        //   - IsTabStop:                 interactive elements (rule A006)
        var fetchTasks = allElements
            .Where(e => e.IsVisible && (TextElementSet.Contains(e.Type) || ImageElementSet.Contains(e.Type) || InteractiveElementSet.Contains(e.Type)))
            .Select(async element =>
            {
                var propsToFetch = new List<string>();
                if (TextElementSet.Contains(element.Type))
                    propsToFetch.AddRange(ColorFontProps);
                if (ImageElementSet.Contains(element.Type))
                    propsToFetch.Add("Source");
                if (InteractiveElementSet.Contains(element.Type))
                    propsToFetch.Add("IsTabStop");

                var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var propTasks = propsToFetch.Distinct().Select(async prop =>
                {
                    var val = await client.GetPropertyAsync(element.Id, prop, ct);
                    return (prop, val);
                });
                var results = await Task.WhenAll(propTasks);
                foreach (var (prop, val) in results)
                    props[prop] = val;
                return (Element: element, Properties: props);
            });

        var fetchedElements = (await Task.WhenAll(fetchTasks)).ToList();

        // Merge: non-fetched elements get an empty props dict
        var fetchedById = fetchedElements.ToDictionary(f => f.Element.Id, f => f.Properties);
        var flatElements = allElements
            .Select(e => (Element: e, Properties: fetchedById.TryGetValue(e.Id, out var p) ? p : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        // Use last-wins for duplicate IDs (can happen with multiple windows)
        var elementLookup = new Dictionary<string, (DevFlowElementInfo Element, Dictionary<string, string?> Properties)>();
        foreach (var item in flatElements)
            elementLookup[item.Element.Id] = item;
        var context = new AuditContext(flatElements, elementLookup, nativeA11yLookup);
        var issues = new List<AccessibilityIssue>();

        foreach (var rule in _rules)
        {
            issues.AddRange(rule.Evaluate(context));
        }

        using var nativeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        nativeCts.CancelAfter(TimeSpan.FromSeconds(6));
        var nativeTree = await client.GetNativeA11yTreeAsync(ct: nativeCts.Token);
        var screenReaderOrder = BuildScreenReaderOrderFromNative(
            nativeTree?.Entries ?? new(),
            elementLookup,
            issues);

        // Build contrast results (with parent chain lookup for inherited backgrounds)
        var contrastResults = BuildContrastResults(flatElements, elementLookup);

        // Calculate score
        var (score, categories) = CalculateScore(flatElements, issues, contrastResults);

        return new AccessibilityAuditResult
        {
            Timestamp = DateTimeOffset.UtcNow,
            TotalElements = allElements.Count,
            Issues = issues.OrderBy(i => i.Severity).ThenBy(i => i.RuleId).ToList(),
            ScreenReaderOrder = screenReaderOrder,
            ContrastResults = contrastResults,
            Score = score,
            ScoreCategories = categories,
        };
    }

    // --- Screen Reader Order (Native) ---

    /// <summary>
    /// Builds screen reader order directly from the native platform accessibility tree.
    /// Elements are in the exact order VoiceOver/TalkBack/Narrator visits them.
    /// For elements matched back to a MAUI ID, marks them if they have issues.
    /// </summary>
    private static List<ScreenReaderEntry> BuildScreenReaderOrderFromNative(
        List<DevFlowNativeA11yEntry> nativeEntries,
        Dictionary<string, (DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elementLookup,
        List<AccessibilityIssue> issues)
    {
        var issueElementIds = new HashSet<string>(issues.Select(i => i.ElementId));
        var result = new List<ScreenReaderEntry>();

        foreach (var entry in nativeEntries)
        {
            // Try to look up the corresponding MAUI element for richer data
            var elementId = entry.ElementId ?? string.Empty;
            elementLookup.TryGetValue(elementId, out var maui);

            result.Add(new ScreenReaderEntry
            {
                Order         = entry.Order,
                ElementId     = elementId,
                ElementType   = maui.Element?.Type ?? entry.NativeType ?? "Native",
                Role          = entry.Role ?? "none",
                AnnouncedText = entry.Label ?? maui.Element?.Text ?? "(no text)",
                Hint          = entry.Hint,
                HeadingLevel  = entry.IsHeading ? "Heading" : null,
                IsInteractive = entry.Traits?.Any(t => t is "Button" or "Link" or "SearchField" or "Slider") == true,
                HasIssue      = !string.IsNullOrEmpty(elementId) && issueElementIds.Contains(elementId),
                WindowBounds  = maui.Element?.WindowBounds ?? entry.WindowBounds,
            });
        }

        return result;
    }

    /// <summary>
    /// Enriches heuristic screen reader entries with native accessibility data (labels, roles, traits).
    /// The heuristic path provides reliable bounds and ordering; native data provides accurate labels.
    /// </summary>
    private static void EnrichWithNativeData(
        List<ScreenReaderEntry> entries,
        Dictionary<string, DevFlowAccessibilityElement> nativeLookup)
    {
        foreach (var entry in entries)
        {
            if (!nativeLookup.TryGetValue(entry.ElementId, out var native))
                continue;

            var a11y = native.Accessibility;
            if (a11y == null) continue;

            // Override announced text with native label (what the screen reader actually says)
            if (!string.IsNullOrEmpty(a11y.Label))
                entry.AnnouncedText = a11y.Label;

            // Override role with native role
            if (!string.IsNullOrEmpty(a11y.Role))
                entry.Role = a11y.Role;

            // Override hint with native hint
            if (!string.IsNullOrEmpty(a11y.Hint))
                entry.Hint = a11y.Hint;

            // Native heading info
            if (a11y.IsHeading)
                entry.HeadingLevel = "Heading";

            // Native interactivity from traits
            if (a11y.IsFocusable || (a11y.Traits?.Any(t =>
                    t is "Button" or "Clickable" or "Link" or "Adjustable" or "SearchField") == true))
                entry.IsInteractive = true;
        }
    }

    // --- Screen Reader Order (Heuristic Fallback) ---

    private static List<ScreenReaderEntry> BuildScreenReaderOrder(
        List<(DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elements,
        List<AccessibilityIssue> issues)
    {
        var issueElementIds = new HashSet<string>(issues.Select(i => i.ElementId));
        var order = new List<ScreenReaderEntry>();
        int index = 0;

        // Elements are already in visual tree order (DFS), which matches screen reader order
        foreach (var (element, props) in elements)
        {
            // Skip layout containers that screen readers skip
            if (IsLayoutContainer(element.Type) && !HasSemanticRole(element, props))
                continue;

            var role = GetScreenReaderRole(element, props);
            var announced = GetAnnouncedText(element, props);

            // Skip elements with no announceable content and no role
            if (string.IsNullOrWhiteSpace(announced) && role == "none")
                continue;

            // Use native a11y data for hint and heading (more accurate than fetched props)
            var hint = element.Accessibility?.Hint;
            var isHeading = element.Accessibility?.IsHeading == true;

            order.Add(new ScreenReaderEntry
            {
                Order = ++index,
                ElementId = element.Id,
                ElementType = element.Type,
                Role = role,
                AnnouncedText = announced ?? "(no text)",
                Hint = hint,
                HeadingLevel = isHeading ? "Heading" : null,
                IsInteractive = IsInteractive(element),
                HasIssue = issueElementIds.Contains(element.Id),
                WindowBounds = element.WindowBounds,
            });
        }

        return order;
    }

    private static bool IsLayoutContainer(string type) =>
        type is "StackLayout" or "VerticalStackLayout" or "HorizontalStackLayout"
            or "Grid" or "FlexLayout" or "AbsoluteLayout" or "RelativeLayout"
            or "ContentView" or "Frame" or "Border" or "ScrollView"
            or "ContentPage" or "Shell" or "NavigationPage" or "TabbedPage"
            or "FlyoutPage";

    private static bool HasSemanticRole(DevFlowElementInfo element, Dictionary<string, string?> props)
    {
        if (!string.IsNullOrWhiteSpace(element.AutomationId)) return true;
        if (props.TryGetValue("SemanticProperties.Description", out var desc) && !string.IsNullOrWhiteSpace(desc)) return true;
        return false;
    }

    private static string GetScreenReaderRole(DevFlowElementInfo element, Dictionary<string, string?> props)
    {
        var type = element.Type;
        if (type is "Button" or "ImageButton" or "SwipeItem") return "Button";
        if (type is "Entry" or "Editor" or "SearchBar") return "Text Field";
        if (type is "Switch" or "CheckBox") return "Toggle";
        if (type is "Slider" or "Stepper") return "Adjustable";
        if (type is "Picker" or "DatePicker" or "TimePicker") return "Picker";
        if (type is "RadioButton") return "Radio Button";
        if (type is "Image" or "ImageButton") return "Image";
        if (type is "Label" or "Span") return "Text";
        if (type is "ActivityIndicator") return "Activity Indicator";
        if (type is "ProgressBar") return "Progress";
        if (type is "WebView") return "Web Content";
        if (element.Gestures?.Any(g => g.Contains("Tap", StringComparison.OrdinalIgnoreCase)) == true) return "Button";
        return "none";
    }

    private static string? GetAnnouncedText(DevFlowElementInfo element, Dictionary<string, string?> props)
    {
        // Native label is what the screen reader actually announces (combines SemanticProperties + platform)
        if (!string.IsNullOrWhiteSpace(element.Accessibility?.Label)) return element.Accessibility!.Label;
        if (!string.IsNullOrWhiteSpace(element.Text)) return element.Text;
        if (!string.IsNullOrWhiteSpace(element.AutomationId)) return element.AutomationId;
        if (element.NativeProperties != null)
        {
            if (element.NativeProperties.TryGetValue("AccessibilityLabel", out var iosLabel) && !string.IsNullOrWhiteSpace(iosLabel)) return iosLabel;
            if (element.NativeProperties.TryGetValue("ContentDescription", out var androidDesc) && !string.IsNullOrWhiteSpace(androidDesc)) return androidDesc;
        }
        return null;
    }

    // --- Color Contrast ---

    private static List<ContrastCheckResult> BuildContrastResults(
        List<(DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elements,
        Dictionary<string, (DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elementLookup)
    {
        var results = new List<ContrastCheckResult>();

        foreach (var (element, props) in elements)
        {
            if (!IsType(element, TextElementTypes))
                continue;

            // Prefer native effective colors (populated by platform tree walker after theme/style resolution).
            // Fall back to fetched MAUI properties for agents that predate this feature.
            var fg = element.EffectiveTextColor
                ?? (props.TryGetValue("TextColor", out var tc) ? tc : null);
            if (string.IsNullOrWhiteSpace(fg))
                continue;

            // Resolve background: effective native color, then fetched props, then parent chain
            var bg = element.EffectiveBackgroundColor
                ?? ResolveBackground(element, props, elementLookup);
            if (string.IsNullOrWhiteSpace(bg))
                continue;

            var fgRgb = ParseColor(fg);
            var bgRgb = ParseColor(bg);
            if (fgRgb == null || bgRgb == null) continue;

            var ratio = CalculateContrastRatio(fgRgb.Value, bgRgb.Value);
            props.TryGetValue("FontSize", out var fontSizeStr);
            var isLargeText = double.TryParse(fontSizeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var fs) && fs >= 18;

            results.Add(new ContrastCheckResult
            {
                ElementId = element.Id,
                ElementType = element.Type,
                ElementText = element.Text,
                ForegroundColor = fg,
                BackgroundColor = bg,
                ContrastRatio = Math.Round(ratio, 2),
                PassesAA = isLargeText ? ratio >= 3.0 : ratio >= 4.5,
                PassesAAA = isLargeText ? ratio >= 4.5 : ratio >= 7.0,
                IsLargeText = isLargeText,
                WindowBounds = element.WindowBounds,
            });
        }

        return results;
    }

    /// <summary>
    /// Resolves the effective background color by walking up the parent chain.
    /// In MAUI, Background (Brush) and BackgroundColor (Color) are separate properties.
    /// Colors are often set via styles/ResourceDictionary on parent containers.
    /// </summary>
    private static string? ResolveBackground(
        DevFlowElementInfo element,
        Dictionary<string, string?> props,
        Dictionary<string, (DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elementLookup)
    {
        // First check the element itself
        var selfBg = GetBgFromProps(props);
        if (selfBg != null) return selfBg;

        // Walk up parent chain to find nearest ancestor with a background
        var currentId = element.ParentId;
        var depth = 0;
        while (currentId != null && depth < 20)
        {
            if (elementLookup.TryGetValue(currentId, out var parent))
            {
                var parentBg = GetBgFromProps(parent.Properties);
                if (parentBg != null) return parentBg;
                currentId = parent.Element.ParentId;
            }
            else break;
            depth++;
        }

        return null;
    }

    /// <summary>
    /// Checks both Background (Brush) and BackgroundColor (Color) properties.
    /// </summary>
    private static string? GetBgFromProps(Dictionary<string, string?> props)
    {
        // Check BackgroundColor first (more specific, Color type)
        if (props.TryGetValue("BackgroundColor", out var bgColor) && !string.IsNullOrWhiteSpace(bgColor) && ParseColor(bgColor) != null)
            return bgColor;
        // Fall back to Background (Brush type)
        if (props.TryGetValue("Background", out var bg) && !string.IsNullOrWhiteSpace(bg) && ParseColor(bg) != null)
            return bg;
        return null;
    }

    internal static (double R, double G, double B)? ParseColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        color = color.Trim();

        // Handle "Color [A=1, R=0.2, G=0.3, B=0.4]" format from MAUI
        if (color.StartsWith("Color [", StringComparison.OrdinalIgnoreCase) || color.StartsWith("["))
        {
            var start = color.IndexOf('[') + 1;
            var end = color.IndexOf(']');
            if (start > 0 && end > start)
            {
                var parts = color[start..end].Split(',');
                double r = 0, g = 0, b = 0;
                foreach (var part in parts)
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        var key = kv[0].Trim();
                        if (double.TryParse(kv[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                        {
                            if (key == "R") r = val;
                            else if (key == "G") g = val;
                            else if (key == "B") b = val;
                        }
                    }
                }
                return (r, g, b);
            }
        }

        // Handle hex: #RRGGBB or #AARRGGBB
        if (color.StartsWith('#'))
        {
            var hex = color[1..];
            if (hex.Length == 8) hex = hex[2..]; // skip alpha
            if (hex.Length == 6 &&
                int.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return (r / 255.0, g / 255.0, b / 255.0);
            }
        }

        // Named colors (common ones)
        return color.ToLowerInvariant() switch
        {
            "black" => (0, 0, 0),
            "white" => (1, 1, 1),
            "red" => (1, 0, 0),
            "green" => (0, 0.502, 0),
            "blue" => (0, 0, 1),
            "yellow" => (1, 1, 0),
            "gray" or "grey" => (0.502, 0.502, 0.502),
            "transparent" => null,
            _ => null,
        };
    }

    internal static double CalculateContrastRatio((double R, double G, double B) fg, (double R, double G, double B) bg)
    {
        var l1 = RelativeLuminance(fg.R, fg.G, fg.B);
        var l2 = RelativeLuminance(bg.R, bg.G, bg.B);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(double r, double g, double b)
    {
        static double Linearize(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    // --- Accessibility Score ---

    private static (int Score, List<AccessibilityScoreCategory> Categories) CalculateScore(
        List<(DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elements,
        List<AccessibilityIssue> issues,
        List<ContrastCheckResult> contrastResults)
    {
        var categories = new List<AccessibilityScoreCategory>();
        var issuesByRule = issues.ToLookup(i => i.RuleId);

        // Labels & Descriptions (A001, A002, A003)
        var labelElements = elements.Count(e =>
            IsType(e.Element, ImageTypes) || IsType(e.Element, ButtonTypes) || IsType(e.Element, InputTypes));
        var labelIssues = issuesByRule["A001"].Count() + issuesByRule["A002"].Count() + issuesByRule["A003"].Count();
        categories.Add(new AccessibilityScoreCategory
        {
            Name = "Labels",
            Icon = "fa-tag",
            Total = Math.Max(1, labelElements),
            Passed = Math.Max(0, labelElements - labelIssues),
        });

        // Touch Targets (A004)
        var interactiveCount = elements.Count(e => IsInteractive(e.Element));
        var touchIssues = issuesByRule["A004"].Count();
        categories.Add(new AccessibilityScoreCategory
        {
            Name = "Touch Targets",
            Icon = "fa-hand-pointer",
            Total = Math.Max(1, interactiveCount),
            Passed = Math.Max(0, interactiveCount - touchIssues),
        });

        // Contrast
        var contrastTotal = contrastResults.Count;
        var contrastPassed = contrastResults.Count(c => c.PassesAA);
        if (contrastTotal > 0)
        {
            categories.Add(new AccessibilityScoreCategory
            {
                Name = "Contrast",
                Icon = "fa-circle-half-stroke",
                Total = contrastTotal,
                Passed = contrastPassed,
            });
        }

        // Keyboard / Focus (A006)
        var focusIssues = issuesByRule["A006"].Count();
        categories.Add(new AccessibilityScoreCategory
        {
            Name = "Keyboard",
            Icon = "fa-keyboard",
            Total = Math.Max(1, interactiveCount),
            Passed = Math.Max(0, interactiveCount - focusIssues),
        });

        // Headings (A007)
        var headingCandidates = issuesByRule["A007"].Count();
        var headingsSet = elements.Count(e => e.Element.Accessibility?.IsHeading == true);
        var headingTotal = headingCandidates + headingsSet;
        if (headingTotal > 0)
        {
            categories.Add(new AccessibilityScoreCategory
            {
                Name = "Headings",
                Icon = "fa-heading",
                Total = headingTotal,
                Passed = headingsSet,
            });
        }

        // Unique IDs (A005)
        var dupIssues = issuesByRule["A005"].Count();
        var totalWithId = elements.Count(e => !string.IsNullOrWhiteSpace(e.Element.AutomationId));
        if (totalWithId > 0)
        {
            categories.Add(new AccessibilityScoreCategory
            {
                Name = "Unique IDs",
                Icon = "fa-fingerprint",
                Total = totalWithId,
                Passed = Math.Max(0, totalWithId - dupIssues),
            });
        }

        // Calculate overall score as weighted average
        var totalWeight = categories.Sum(c => c.Total);
        var totalPassed = categories.Sum(c => c.Passed);
        var score = totalWeight > 0 ? (int)Math.Round(100.0 * totalPassed / totalWeight) : 100;

        return (score, categories);
    }

    // --- Helpers ---

    private static List<DevFlowElementInfo> FlattenTree(List<DevFlowElementInfo> tree)
    {
        var result = new List<DevFlowElementInfo>();
        foreach (var node in tree)
            FlattenRecursive(node, result);
        return result;
    }

    private static void FlattenRecursive(DevFlowElementInfo node, List<DevFlowElementInfo> result)
    {
        result.Add(node);
        if (node.Children != null)
        {
            foreach (var child in node.Children)
                FlattenRecursive(child, result);
        }
    }

    internal static bool IsType(DevFlowElementInfo element, string[] types)
        => types.Any(t => element.Type.Equals(t, StringComparison.OrdinalIgnoreCase));

    internal static bool HasDescription(DevFlowElementInfo element, Dictionary<string, string?> props, AuditContext? context = null)
    {
        // Native label is the most reliable source (combines SemanticProperties + platform accessibility)
        if (!string.IsNullOrWhiteSpace(element.Accessibility?.Label)) return true;
        // Fallback for older agents without embedded accessibility data
        if (context != null && !string.IsNullOrWhiteSpace(context.GetNativeLabel(element.Id))) return true;

        if (!string.IsNullOrWhiteSpace(element.AutomationId)) return true;
        if (!string.IsNullOrWhiteSpace(element.Text)) return true;
        if (element.NativeProperties != null)
        {
            if (element.NativeProperties.TryGetValue("accessibilityLabel", out var iosLabel) && !string.IsNullOrWhiteSpace(iosLabel)) return true;
            if (element.NativeProperties.TryGetValue("contentDescription", out var androidDesc) && !string.IsNullOrWhiteSpace(androidDesc)) return true;
        }
        return false;
    }

    internal static bool HasSemanticHint(DevFlowElementInfo element, Dictionary<string, string?> props)
    {
        // Native hint is set from SemanticProperties.Hint by the agent
        if (!string.IsNullOrWhiteSpace(element.Accessibility?.Hint)) return true;
        return props.TryGetValue("SemanticProperties.Hint", out var hint) && !string.IsNullOrWhiteSpace(hint);
    }

    internal static bool IsInteractive(DevFlowElementInfo element)
        => IsType(element, InteractiveTypes)
           || (element.Gestures?.Any(g => g.Contains("Tap", StringComparison.OrdinalIgnoreCase)) == true);

    internal static AccessibilityIssue CreateIssue(
        DevFlowElementInfo element,
        AccessibilitySeverity severity,
        string ruleId,
        string ruleName,
        string message,
        string suggestion,
        string? xamlFix = null)
    {
        return new AccessibilityIssue
        {
            ElementId = element.Id,
            ElementType = element.Type,
            ElementText = element.Text,
            AutomationId = element.AutomationId,
            Severity = severity,
            RuleId = ruleId,
            RuleName = ruleName,
            Message = message,
            Suggestion = suggestion,
            XamlFix = xamlFix,
            WindowBounds = element.WindowBounds,
        };
    }
}

/// <summary>
/// Context passed to each audit rule containing all elements and their fetched properties.
/// </summary>
public class AuditContext
{
    public List<(DevFlowElementInfo Element, Dictionary<string, string?> Properties)> Elements { get; }
    public Dictionary<string, (DevFlowElementInfo Element, Dictionary<string, string?> Properties)> ElementLookup { get; }
    public Dictionary<string, DevFlowAccessibilityElement> NativeAccessibility { get; }

    public AuditContext(
        List<(DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elements,
        Dictionary<string, (DevFlowElementInfo Element, Dictionary<string, string?> Properties)> elementLookup,
        Dictionary<string, DevFlowAccessibilityElement>? nativeAccessibility = null)
    {
        Elements = elements;
        ElementLookup = elementLookup;
        NativeAccessibility = nativeAccessibility ?? new();
    }

    /// <summary>
    /// Gets the native accessibility label for an element, if available.
    /// </summary>
    public string? GetNativeLabel(string elementId)
    {
        return NativeAccessibility.TryGetValue(elementId, out var el)
            ? el.Accessibility?.Label
            : null;
    }

    /// <summary>
    /// Returns true if native data confirms this element is an accessibility element.
    /// </summary>
    public bool? IsNativeAccessibilityElement(string elementId)
    {
        return NativeAccessibility.TryGetValue(elementId, out var el)
            ? el.Accessibility?.IsAccessibilityElement
            : null;
    }
}

/// <summary>
/// Interface for an accessibility audit rule.
/// </summary>
public interface IAccessibilityRule
{
    IEnumerable<AccessibilityIssue> Evaluate(AuditContext context);
}

// --- Rule Implementations ---

internal class MissingImageDescriptionRule : IAccessibilityRule
{
    private static readonly string[] Types = { "Image", "ImageButton" };

    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!AccessibilityAuditService.IsType(element, Types))
                continue;
            if (props.TryGetValue("Source", out var source) && string.IsNullOrWhiteSpace(source))
                continue;
            if (!AccessibilityAuditService.HasDescription(element, props, context))
            {
                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Error,
                    "A001",
                    "Missing Image Description",
                    $"{element.Type} has no accessible description. Screen readers cannot describe this image.",
                    "Add SemanticProperties.Description to provide alt text, or set AutomationId for test identification.",
                    $"SemanticProperties.Description=\"[describe this {element.Type.ToLowerInvariant()}]\"");
            }
        }
    }
}

internal class MissingButtonLabelRule : IAccessibilityRule
{
    private static readonly string[] Types = { "Button", "ImageButton", "SwipeItem" };

    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!AccessibilityAuditService.IsType(element, Types))
                continue;
            if (!AccessibilityAuditService.HasDescription(element, props, context))
            {
                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Error,
                    "A002",
                    "Missing Button Label",
                    $"{element.Type} has no accessible label. Screen readers will announce it as an unlabeled button.",
                    "Set the Text property or add SemanticProperties.Description.",
                    $"SemanticProperties.Description=\"[button action]\"");
            }
        }
    }
}

internal class MissingInputLabelRule : IAccessibilityRule
{
    private static readonly string[] Types = { "Entry", "Editor", "SearchBar", "DatePicker", "TimePicker", "Picker" };

    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!AccessibilityAuditService.IsType(element, Types))
                continue;
            if (!AccessibilityAuditService.HasDescription(element, props, context) && !AccessibilityAuditService.HasSemanticHint(element, props))
            {
                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Error,
                    "A003",
                    "Missing Input Label",
                    $"{element.Type} has no accessible label or hint. Users won't know what to enter.",
                    "Add SemanticProperties.Description (label) and/or SemanticProperties.Hint (usage hint).",
                    $"SemanticProperties.Description=\"[field label]\"\nSemanticProperties.Hint=\"[e.g. Enter your email address]\"");
            }
        }
    }
}

internal class TouchTargetTooSmallRule : IAccessibilityRule
{
    private const double MinSize = 44;

    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!AccessibilityAuditService.IsInteractive(element))
                continue;

            var bounds = element.WindowBounds ?? element.Bounds;
            if (bounds == null || bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            if (bounds.Width < MinSize || bounds.Height < MinSize)
            {
                var wFix = bounds.Width < MinSize ? $"MinimumWidthRequest=\"{MinSize}\"" : "";
                var hFix = bounds.Height < MinSize ? $"MinimumHeightRequest=\"{MinSize}\"" : "";
                var fix = string.Join(" ", new[] { wFix, hFix }.Where(s => s.Length > 0));

                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Warning,
                    "A004",
                    "Touch Target Too Small",
                    $"{element.Type} size is {bounds.Width:F0}x{bounds.Height:F0} dp. Minimum recommended is {MinSize}x{MinSize} dp.",
                    "Increase WidthRequest/HeightRequest or add Padding to meet the 44x44 dp minimum touch target.",
                    fix);
            }
        }
    }
}

internal class DuplicateAutomationIdRule : IAccessibilityRule
{
    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        var grouped = context.Elements
            .Where(e => !string.IsNullOrWhiteSpace(e.Element.AutomationId))
            .GroupBy(e => e.Element.AutomationId!)
            .Where(g => g.Count() > 1);

        foreach (var group in grouped)
        {
            foreach (var (element, props) in group)
            {
                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Error,
                    "A005",
                    "Duplicate AutomationId",
                    $"AutomationId \"{element.AutomationId}\" is used by {group.Count()} elements. AutomationIds must be unique.",
                    "Assign a unique AutomationId to each element for reliable accessibility and test identification.",
                    $"AutomationId=\"{element.AutomationId}_{element.Type.ToLowerInvariant()}\"");
            }
        }
    }
}

internal class NotFocusableInteractiveRule : IAccessibilityRule
{
    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!AccessibilityAuditService.IsInteractive(element))
                continue;

            if (props.TryGetValue("IsTabStop", out var isTabStop)
                && bool.TryParse(isTabStop, out var val)
                && !val)
            {
                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Warning,
                    "A006",
                    "Not Keyboard Focusable",
                    $"{element.Type} has IsTabStop=false. Keyboard and switch-access users cannot reach it.",
                    "Set IsTabStop=\"true\" unless the element is intentionally unreachable.",
                    "IsTabStop=\"True\"");
            }
        }
    }
}

internal class MissingHeadingLevelRule : IAccessibilityRule
{
    private const double HeadingFontSizeThreshold = 20;

    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!element.Type.Equals("Label", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!props.TryGetValue("FontSize", out var fontSizeStr) || !double.TryParse(fontSizeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fontSize))
                continue;

            if (fontSize < HeadingFontSizeThreshold)
                continue;

            // Skip if native a11y already marks it as heading, or if SemanticProperties.HeadingLevel is set
            if (element.Accessibility?.IsHeading == true)
                continue;

            yield return AccessibilityAuditService.CreateIssue(
                element,
                AccessibilitySeverity.Info,
                "A007",
                "Missing Heading Level",
                $"Label with FontSize={fontSize:F0} looks like a heading but has no SemanticProperties.HeadingLevel.",
                "Add SemanticProperties.HeadingLevel (Level1-Level9) so screen readers can navigate by headings.",
                "SemanticProperties.HeadingLevel=\"Level1\"");
        }
    }
}

internal class LowContrastRule : IAccessibilityRule
{
    private static readonly string[] TextTypes = { "Label", "Button", "Entry", "Editor", "SearchBar", "Span", "RadioButton" };

    public IEnumerable<AccessibilityIssue> Evaluate(AuditContext context)
    {
        foreach (var (element, props) in context.Elements)
        {
            if (!AccessibilityAuditService.IsType(element, TextTypes))
                continue;

            if (!props.TryGetValue("TextColor", out var fg) || string.IsNullOrWhiteSpace(fg))
                continue;

            // Resolve background from parent chain (handles styles on containers)
            var bg = ResolveBackgroundFromContext(element, props, context);
            if (string.IsNullOrWhiteSpace(bg))
                continue;

            var fgRgb = AccessibilityAuditService.ParseColor(fg);
            var bgRgb = AccessibilityAuditService.ParseColor(bg);
            if (fgRgb == null || bgRgb == null) continue;

            var ratio = AccessibilityAuditService.CalculateContrastRatio(fgRgb.Value, bgRgb.Value);

            props.TryGetValue("FontSize", out var fontSizeStr);
            var isLargeText = double.TryParse(fontSizeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var fs) && fs >= 18;
            var required = isLargeText ? 3.0 : 4.5;

            if (ratio < required)
            {
                yield return AccessibilityAuditService.CreateIssue(
                    element,
                    AccessibilitySeverity.Warning,
                    "A008",
                    "Low Color Contrast",
                    $"{element.Type} contrast ratio is {ratio:F1}:1 (requires {required:F1}:1 for WCAG AA{(isLargeText ? " large text" : "")}).",
                    "Change TextColor or Background to achieve sufficient contrast. Use a contrast checker to find accessible color pairs.");
            }
        }
    }

    private static string? ResolveBackgroundFromContext(DevFlowElementInfo element, Dictionary<string, string?> props, AuditContext context)
    {
        var selfBg = GetBg(props);
        if (selfBg != null) return selfBg;

        var currentId = element.ParentId;
        var depth = 0;
        while (currentId != null && depth < 20)
        {
            if (context.ElementLookup.TryGetValue(currentId, out var parent))
            {
                var parentBg = GetBg(parent.Properties);
                if (parentBg != null) return parentBg;
                currentId = parent.Element.ParentId;
            }
            else break;
            depth++;
        }

        return null;
    }

    private static string? GetBg(Dictionary<string, string?> props)
    {
        if (props.TryGetValue("BackgroundColor", out var bgColor) && !string.IsNullOrWhiteSpace(bgColor) && AccessibilityAuditService.ParseColor(bgColor) != null)
            return bgColor;
        if (props.TryGetValue("Background", out var bg) && !string.IsNullOrWhiteSpace(bg) && AccessibilityAuditService.ParseColor(bg) != null)
            return bg;
        return null;
    }
}
