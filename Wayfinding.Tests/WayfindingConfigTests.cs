using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class WayfindingConfigTests
{
    [Fact]
    public void Defaults_are_already_valid()
    {
        var config = new WayfindingClientConfig();

        Assert.False(WayfindingConfig.Normalize(config));
        Assert.Equal(WayfindingConfig.DefaultKnownColor, config.knownColor);
        Assert.Equal(WayfindingConfig.DefaultUnknownColor, config.unknownColor);
        Assert.Equal(WayfindingConfig.DefaultBuildColor, config.buildColor);
        Assert.Equal(WayfindingConfig.DefaultKnownThickness, config.knownThickness);
        Assert.Equal(WayfindingConfig.DefaultUnknownThickness, config.unknownThickness);
        Assert.Equal(WayfindingConfig.DefaultBuildThickness, config.buildThickness);
        Assert.True(config.showOnMinimap);
        Assert.True(config.showOnWorldMap);
        Assert.Equal(WayfindingClientConfig.ClickCtrlLeft, config.clickBinding);
        Assert.Equal(WayfindingConfig.DefaultWaypointRadius, config.waypointClickRadius);
        Assert.Equal(WayfindingConfig.DefaultPlayerRadius, config.playerClickRadius);
    }

    [Fact]
    public void Bad_values_fall_back_or_clamp_and_valid_values_stay()
    {
        var config = new WayfindingClientConfig
        {
            debugVerbosity = 9,
            knownColor = "nope",
            unknownColor = "1.4,-0.2,0.5,2",
            buildColor = "0.10,0.20,0.30,1",
            knownThickness = double.NaN,
            unknownThickness = 100,
            buildThickness = 0.1,
            showOnMinimap = false,
            showOnWorldMap = false,
            clickBinding = "Middle",
            waypointClickRadius = float.PositiveInfinity,
            playerClickRadius = 0,
        };

        Assert.True(WayfindingConfig.Normalize(config));

        Assert.Equal(3, config.debugVerbosity);
        Assert.Equal(WayfindingConfig.DefaultKnownColor, config.knownColor);
        Assert.Equal("1,0,0.5,1", config.unknownColor);
        Assert.Equal("0.1,0.2,0.3,1", config.buildColor);
        Assert.Equal(WayfindingConfig.DefaultKnownThickness, config.knownThickness);
        Assert.Equal(WayfindingConfig.MaxThickness, config.unknownThickness);
        Assert.Equal(WayfindingConfig.MinThickness, config.buildThickness);
        Assert.False(config.showOnMinimap);
        Assert.False(config.showOnWorldMap);
        Assert.Equal(WayfindingClientConfig.ClickMiddle, config.clickBinding);
        Assert.Equal(WayfindingConfig.DefaultWaypointRadius, config.waypointClickRadius);
        Assert.Equal(WayfindingConfig.MinRadius, config.playerClickRadius);
    }

    [Fact]
    public void A_missing_binding_uses_ctrl_left()
    {
        var config = new WayfindingClientConfig { clickBinding = "" };

        Assert.True(WayfindingConfig.Normalize(config));
        Assert.Equal(WayfindingClientConfig.ClickCtrlLeft, config.clickBinding);
    }
}
