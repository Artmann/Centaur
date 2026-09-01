using Centaur.App;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The settings registry is what the page, the search box and every future option are rendered
/// from, so its invariants are worth asserting: a descriptor with a duplicate id would have its
/// changes attributed to the wrong setting, and one with no title would render as a blank row.
/// </summary>
public class SettingsRegistryTests
{
    [Fact]
    public void Every_setting_has_a_unique_id()
    {
        var ids = SettingsRegistry.All.Select(s => s.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void Every_setting_is_renderable()
    {
        Assert.NotEmpty(SettingsRegistry.All);

        foreach (var setting in SettingsRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(setting.Title), setting.Id);
            Assert.False(string.IsNullOrWhiteSpace(setting.Description), setting.Id);
            Assert.False(string.IsNullOrWhiteSpace(setting.Section), setting.Id);
            Assert.True(Enum.IsDefined(setting.Tab), setting.Id);
        }
    }

    [Fact]
    public void Both_tabs_have_settings_on_them()
    {
        Assert.NotEmpty(SettingsRegistry.ForTab(SettingsTab.General));
        Assert.NotEmpty(SettingsRegistry.ForTab(SettingsTab.Appearance));
    }

    [Fact]
    public void An_empty_query_keeps_everything_in_registry_order()
    {
        var results = SettingsSearch.Filter(SettingsRegistry.All, "   ");

        Assert.Equal(SettingsRegistry.All, results);
    }

    [Fact]
    public void Search_finds_a_setting_by_its_title()
    {
        var results = SettingsSearch.Filter(SettingsRegistry.All, "font size");

        Assert.Equal(SettingIds.FontSize, results[0].Id);
    }

    [Fact]
    public void Search_finds_a_setting_by_a_keyword_its_title_does_not_contain()
    {
        var results = SettingsSearch.Filter(SettingsRegistry.All, "transparency");

        Assert.Contains(results, s => s.Id == SettingIds.WindowOpacity);
    }

    [Fact]
    public void Search_finds_a_setting_by_its_section()
    {
        var results = SettingsSearch.Filter(SettingsRegistry.All, "cursor");

        Assert.Contains(results, s => s.Id == SettingIds.CursorStyle);
        Assert.Contains(results, s => s.Id == SettingIds.CursorBlink);
    }

    /// <summary>Results span both tabs, which is the whole reason the page ignores the sidebar
    /// while a query is live.</summary>
    [Fact]
    public void Search_spans_both_tabs()
    {
        var results = SettingsSearch.Filter(SettingsRegistry.All, "s");

        Assert.Contains(results, s => s.Tab == SettingsTab.General);
        Assert.Contains(results, s => s.Tab == SettingsTab.Appearance);
    }

    [Fact]
    public void A_nonsense_query_matches_nothing()
    {
        Assert.Empty(SettingsSearch.Filter(SettingsRegistry.All, "qqzzxwvj"));
    }
}
