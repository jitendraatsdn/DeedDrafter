using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeedDrafter
{
  public sealed class DoubleToVisibilityConverter : IValueConverter
  {
    public bool IsReversed { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      var val = System.Convert.ToDouble(value, CultureInfo.InvariantCulture) >= 0;
      if (this.IsReversed)
        val = !val;

      if (val)
        return Visibility.Visible;

      return this.UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }

  public sealed class BooleanToVisibilityConverter : IValueConverter
  {
    public bool IsReversed { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      var val = System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
      if (this.IsReversed)
        val = !val;

      if (val)
        return Visibility.Visible;

      return this.UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }

  // This is currently coded as an "OR" expression
  //
  //usage:
  //
  //  <StackPanel.Visibility>
  //    <MultiBinding Converter="{StaticResource MultiBooleanToVisibilityConverter}">
  //      <Binding Path="IsExpanded" ElementName="DPE_ParcelEntry"/>
  //      <Binding Path="IsExpanded" ElementName="PDE_Tools"/>
  //    </MultiBinding>
  //  </StackPanel.Visibility>

  public sealed class MultiBooleanToVisibilityConverter : IMultiValueConverter
  {
    public bool IsReversed { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
      foreach (object value in values)
      {
        var val = System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        if (this.IsReversed)
          val = !val;

        if (val)
          return Visibility.Visible;
      }
      return this.UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture)
    {
      throw new NotImplementedException();
    }
  }
}
