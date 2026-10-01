using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiSerial.Core.Abstractions;
using DiSerial.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiSerial.App.ViewModels.Panels;

// Represent an automatic reply.
public sealed partial class AutoAnswerRowViewModel(AutoAnswerRule rule) : ObservableObject
{
    public long Id { get; } = rule.Id;

    public AutoAnswerRule Rule { get; } = rule;

    public string Display { get; } = rule.Name;

    public string TriggerText { get; } = rule.TriggerText;

    public string ResponseText { get; } = rule.ResponseText;

    public PayloadFormat TriggerFormat { get; } = rule.TriggerFormat;

    public PayloadFormat ResponseFormat { get; } = rule.ResponseFormat;

    public LineEnding Terminator { get; } = rule.Terminator;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _triggeredCount;
}

// Manage automatic reply rules.
public sealed partial class AutoAnswerPanelViewModel : ObservableObject
{
    private readonly IAutoAnswerStore? _store;
    private readonly ILogger _logger;

    private IReadOnlyList<ArmedRule> _armed = [];

    // Load automatic reply controls.
    public AutoAnswerPanelViewModel(IAutoAnswerStore? store, ILogger? logger = null)
    {
        _store = store;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        Reload();
    }

    public ObservableCollection<AutoAnswerRowViewModel> Rules { get; } = [];

    public bool HasRules => Rules.Count > 0;

    [ObservableProperty]
    private bool _loadSucceeded = true;

    public bool CanEdit => _store is not null;

    [ObservableProperty]
    private AutoAnswerRowViewModel? _selected;

    // Reload automatic reply rules.
    public void Reload()
    {
        Rules.Clear();
        LoadSucceeded = true;

        if (_store is null)
        {
            Rearm();
            return;
        }

        try
        {
            var rules = _store.Load();
            foreach (var rule in rules) Rules.Add(Row(rule));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not read the auto-answer rules; the table is empty.");
            LoadSucceeded = false;
        }

        Rearm();
        OnPropertyChanged(nameof(HasRules));
        NotifyTickCounts();
    }

    // Create a reply rule row.
    private AutoAnswerRowViewModel Row(AutoAnswerRule rule)
    {
        var row = new AutoAnswerRowViewModel(rule);
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AutoAnswerRowViewModel.IsEnabled)) return;

            Rearm();
            NotifyTickCounts();
        };
        return row;
    }

    // Refresh reply trigger counts.
    private void NotifyTickCounts()
    {
        OnPropertyChanged(nameof(AnyEnabled));
        OnPropertyChanged(nameof(AnyDisabled));
    }

    public bool AnyEnabled => Rules.Any(r => r.IsEnabled);

    public bool AnyDisabled => Rules.Any(r => !r.IsEnabled);

    // Prepare enabled reply rules.
    private void Rearm() =>
        _armed = AutoAnswerMatcher.Arm(
            [.. Rules.Select(r => r.Rule)],
            rule => Rules.FirstOrDefault(r => r.Id == rule.Id)?.IsEnabled == true);

    // Match a frame to a reply rule.
    public ArmedRule? Answer(SerialFrame frame)
    {
        if (frame.Direction != FrameDirection.Rx) return null;

        var hit = AutoAnswerMatcher.FirstMatch(frame.Data.Span, _armed);
        if (hit is null) return null;

        var row = Rules.FirstOrDefault(r => r.Id == hit.Id);
        if (row is not null) row.TriggeredCount++;

        return hit;
    }


    // Add an automatic reply rule.
    public AutoAnswerSaveResult Add(
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator)
    {
        if (_store is null) return AutoAnswerSaveResult.NotFound;

        var result = _store.Add(name, triggerFormat, triggerText, responseFormat, responseText, terminator);
        if (result == AutoAnswerSaveResult.Saved) Reload();
        return result;
    }

    // Update an automatic reply rule.
    public AutoAnswerSaveResult Update(
        long id,
        string name,
        PayloadFormat triggerFormat, string triggerText,
        PayloadFormat responseFormat, string responseText,
        LineEnding terminator)
    {
        if (_store is null) return AutoAnswerSaveResult.NotFound;

        var result = _store.Update(id, name, triggerFormat, triggerText, responseFormat, responseText, terminator);
        if (result == AutoAnswerSaveResult.Saved) Reload();
        return result;
    }

    // Delete an automatic reply rule.
    public void Delete(long id)
    {
        if (_store is null) return;
        _store.Delete(id);
        Reload();
    }

    // Move a reply rule to the front.
    public void MoveToFront(long id) => Reorder(s => s.MoveToFront(id));

    // Move a reply rule up one place.
    public void MoveForwardOne(long id) => Reorder(s => s.MoveForwardOne(id));

    // Move a reply rule down one slot.
    public void MoveBackOne(long id) => Reorder(s => s.MoveBackOne(id));

    // Refresh reply rule ordering.
    private void Reorder(Action<IAutoAnswerStore> move)
    {
        if (_store is null) return;
        move(_store);
        Reload();
    }

    // Disable all automatic replies.
    public void DisableAll()
    {
        foreach (var row in Rules) row.IsEnabled = false;
    }

    // Enable all automatic replies.
    public void EnableAll()
    {
        foreach (var row in Rules) row.IsEnabled = true;
    }

    // Reset the reply trigger count.
    public void ClearCount(long id)
    {
        var row = Rules.FirstOrDefault(r => r.Id == id);
        if (row is not null) row.TriggeredCount = 0;
    }
}
