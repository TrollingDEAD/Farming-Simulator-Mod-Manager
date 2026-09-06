using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.Core.Models;

namespace FsModManager.App.ViewModels;

/// <summary>
/// One row in a mod's expanded content list — a single storeItem's shop details, plus any
/// object-level conflicts involving it (same severity-tint + tooltip pattern as a mod row).
/// Compact spec-tag row is always shown; a secondary detail block (brand/mass/functions/fill
/// capacities/configuration groups) is revealed on click when this object actually has any.
/// </summary>
public sealed partial class StoreItemRowViewModel : ObservableObject
{
    public StoreItemRowViewModel(StoreItemDetail detail, IReadOnlyList<ModConflict> objectConflicts, string ownerModInternalName)
    {
        Detail = detail;
        Icon = ImageDecoding.TryDecodeToBitmapSource(detail.ShopImageData);

        HasConflicts = objectConflicts.Count > 0;
        WorstSeverity = objectConflicts.Count == 0 ? null : objectConflicts.Min(c => (ConflictSeverity?)c.Severity);
        ConflictSummary = string.Join(Environment.NewLine, objectConflicts.Select(c =>
        {
            var otherMod = string.Equals(c.ModA, ownerModInternalName, StringComparison.OrdinalIgnoreCase) ? c.ModB : c.ModA;
            return $"[{c.Severity}] Conflicts with {otherMod}: {c.Description}";
        }));

        ConfigurationGroupLabels = detail.ConfigurationGroupCounts
            .Select(g => $"{g.OptionCount} {g.GroupName}{(g.OptionCount == 1 ? "" : " options")}")
            .ToList();

        RawFillCapacityDisplays = detail.RawFillCapacities
            .Select(f => $"{f.FillTypeName}: {f.LiterCapacity:0.#} L")
            .ToList();

        HasSecondaryDetail = HasBrand || HasMass || HasFunctions || HasRawFillCapacities || HasRequiredPowerHp || HasConfigurationGroups;
    }

    public StoreItemDetail Detail { get; }

    public string ObjectDisplayName => Detail.ObjectDisplayName;

    public string PriceDisplay => Detail.Price is { } price ? price.ToString("C0") : "—";

    public IReadOnlyList<SpecEntry> Specs => Detail.Specs;

    public bool HasSpecs => Specs.Count > 0;

    public BitmapSource? Icon { get; }

    public ConflictSeverity? WorstSeverity { get; }

    public bool HasConflicts { get; }

    public string ConflictSummary { get; }

    /// <summary>Whether this object has any secondary detail worth expanding to reveal.</summary>
    public bool HasSecondaryDetail { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (HasSecondaryDetail)
        {
            IsExpanded = !IsExpanded;
        }
    }

    public string? Brand => Detail.Brand;

    public bool HasBrand => !string.IsNullOrWhiteSpace(Brand);

    public string MassDisplay => Detail.MassKg is { } mass ? $"{mass:0.#} kg" : string.Empty;

    public bool HasMass => Detail.MassKg is not null;

    public IReadOnlyList<string> Functions => Detail.Functions;

    public bool HasFunctions => Functions.Count > 0;

    /// <summary>Compact display strings, e.g. "diesel: 1000 L".</summary>
    public IReadOnlyList<string> RawFillCapacityDisplays { get; }

    public bool HasRawFillCapacities => RawFillCapacityDisplays.Count > 0;

    /// <summary>Always false today — see remarks on <see cref="StoreItemDetail.RequiredPowerHp"/>.</summary>
    public bool HasRequiredPowerHp => Detail.RequiredPowerHp is not null;

    public string RequiredPowerDisplay => Detail.RequiredPowerHp is { } hp ? $"{hp:0.#} hp" : string.Empty;

    /// <summary>e.g. "5 Color options", "3 Wheel options".</summary>
    public IReadOnlyList<string> ConfigurationGroupLabels { get; }

    public bool HasConfigurationGroups => ConfigurationGroupLabels.Count > 0;
}

