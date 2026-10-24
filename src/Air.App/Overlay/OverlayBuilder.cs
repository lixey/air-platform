namespace Platform.App.Overlay;

public sealed class OverlayBuilder(string key, string presentation, PanelLayoutDto? layout = null)
{
  private readonly OverlayDto _dto = new() { Key = key, Presentation = presentation, Layout = layout };

  public string Presentation => _dto.Presentation;
  public string? WidthValue => _dto.Width;
  public string? HeightValue => _dto.Height;

  public OverlayBuilder Screen(string servicePath, object? paramsObj = null)
  {
    _dto.Screen = new OverlayScreenDto { Service = servicePath, Params = paramsObj };
    return this;
  }

  public OverlayBuilder Width(string width)
  {
    _dto.Width = width;
    return this;
  }
  public OverlayBuilder Width(int px) => Width(PanelSize.Px(px));
  public OverlayBuilder Height(string height)
  {
    _dto.Height = height;
    return this;
  }
  public OverlayBuilder Height(int px) => Height(PanelSize.Px(px));
  public OverlayBuilder MaxWidth(int px)
  {
    _dto.MaxWidth = PanelSize.Px(px);
    return this;
  }
  public OverlayBuilder Fill()
  {
    _dto.Width = PanelSize.Fill;
    _dto.Height = PanelSize.Fill;
    return this;
  }

  public OverlayDto Build() => _dto;
}
