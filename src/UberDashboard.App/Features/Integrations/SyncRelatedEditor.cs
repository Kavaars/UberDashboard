using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static JsonObject? EditRelated(Window owner, SyncEntityKind kind, JsonObject? original, Action<Window>? onLoaded = null)
    {
        if (kind is not (SyncEntityKind.ChecklistItem or SyncEntityKind.LinkedResource)) throw new ArgumentOutOfRangeException(nameof(kind));
        var window = WindowFrame.Create(owner, UiText.Get(kind == SyncEntityKind.ChecklistItem ? "SyncChecklist" : "SyncLinkedResources"), 620, 550);
        window.MinWidth = 400; window.MinHeight = 340;
        var initial = MicrosoftRelatedDraft.Read(original);
        var form = new StackPanel { Margin = new Thickness(16) };
        var name = Field(form, "SyncRelatedName", initial.DisplayName);
        CheckBox? isChecked = null; TextBox? checkedAt = null, createdAt = null, application = null, url = null, externalId = null;
        if (kind == SyncEntityKind.ChecklistItem)
        {
            isChecked = new CheckBox { Content = UiText.Get("SyncStepChecked"), IsChecked = initial.IsChecked, Margin = new Thickness(0, 10, 0, 6) };
            isChecked.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox"); AutomationProperties.SetName(isChecked, UiText.Get("SyncStepChecked")); form.Children.Add(isChecked);
            var dates = new StackPanel(); var expander = new Expander { Header = UiText.Get("SyncStepDates"), Content = dates };
            AutomationProperties.SetName(expander, UiText.Get("SyncStepDates")); form.Children.Add(expander);
            dates.Children.Add(new TextBlock { Text = UiText.Get("SyncStepTimestampHelp"), TextWrapping = TextWrapping.Wrap });
            checkedAt = Field(dates, "SyncStepCheckedAt", initial.CheckedAt); createdAt = Field(dates, "SyncStepCreatedAt", initial.CreatedAt);
        }
        else
        {
            application = Field(form, "SyncRelatedApplication", original is null ? "UberDashboard" : initial.ApplicationName);
            url = Field(form, "SyncRelatedUrl", initial.WebUrl); url.ToolTip = UiText.Get("SyncRelatedUrlHelp");
            externalId = Field(form, "SyncRelatedExternalId", initial.ExternalId);
            externalId.ToolTip = UiText.Get("SyncRelatedExternalIdHelp");
        }
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        foreach (var input in new[] { name, checkedAt, createdAt, application, url, externalId }.OfType<TextBox>())
            input.TextChanged += (_, _) => validation.Text = "";
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);
        var actions = new WrapPanel(); var save = ActionButton("SyncSaveLocal", actions); var cancel = ActionButton("SyncCancel", actions); cancel.IsCancel = true;
        JsonObject? result = null;
        save.Click += (_, _) =>
        {
            var draft = initial with { DisplayName = name.Text, IsChecked = isChecked?.IsChecked ?? initial.IsChecked,
                CheckedAt = checkedAt?.Text ?? initial.CheckedAt, CreatedAt = createdAt?.Text ?? initial.CreatedAt,
                ApplicationName = application?.Text ?? initial.ApplicationName, WebUrl = url?.Text ?? initial.WebUrl, ExternalId = externalId?.Text ?? initial.ExternalId };
            if (!draft.TryApply(kind, original, out var fields, out var error))
            {
                validation.Text = UiText.Get(error switch { RelatedDraftValidation.NameRequired => "SyncRelatedNameRequired",
                    RelatedDraftValidation.InvalidTimestamp => "SyncStepTimestampInvalid", RelatedDraftValidation.InvalidUrl => "SyncRelatedUrlInvalid", _ => "SyncFailed" }); return;
            }
            result = fields; window.DialogResult = true;
        };
        var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) }; footer.Children.Add(validation); footer.Children.Add(actions);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = layout;
        if (onLoaded is not null) window.Loaded += (_, _) => onLoaded(window);
        return window.ShowDialog() == true ? result : null;
    }
}
