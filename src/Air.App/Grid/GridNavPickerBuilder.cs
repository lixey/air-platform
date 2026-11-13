namespace Platform.App.Grid;

public sealed class GridNavPickerBuilder(GridNavPickerModel model)
{
  public GridNavPickerBuilder MinLength(int len)
  {
    model.MinLength = len;
    return this;
  }

  public GridNavPickerBuilder ReadsParent()
  {
    model.ReadsParent = true;
    return this;
  }
}
