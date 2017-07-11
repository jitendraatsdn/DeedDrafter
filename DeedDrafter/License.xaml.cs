using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DeedDrafter
{
  /// <summary>
  /// Interaction logic for License.xaml
  /// </summary>
  public partial class License : Window
  {
    bool _agreed = false;
    public bool Agreed
    {
      get { return _agreed; }
    }

    public License()
    {
      InitializeComponent();
    }

    public static bool ShowWindow()
    {
      string keySW = "Software";
      string keyESRI = "ESRI";
      string keyDF = "DeedDrafter";
      string keyExecute = "Execute";

      string acceptValue = "{61F78689-7E9A-47CC-B8F0-DC4428AD4937}";

      RegistryKey regKeySW = Registry.CurrentUser.OpenSubKey(keySW);
      if (regKeySW != null)
      {
        RegistryKey regKeyESRI = regKeySW.OpenSubKey(keyESRI);
        if (regKeyESRI != null)
        {
          RegistryKey regKeyDF = regKeyESRI.OpenSubKey(keyDF);
          if (regKeyDF != null)
          {
            object value = regKeyDF.GetValue(keyExecute, "");
            if (value.ToString() == acceptValue)
              return true;
          }
        }
      }

      var license = new License();
      license.ShowDialog();

      if (license.Agreed)
      {
        // If we have any errors, allow the app to start, but 
        // the user will have to accept the agreement again :(
        if (regKeySW == null)
          return true;

        try
        {
          regKeySW = Registry.CurrentUser.OpenSubKey(keySW, RegistryKeyPermissionCheck.ReadWriteSubTree);
          if (regKeySW == null)
            return true;

          RegistryKey regKeyESRI = regKeySW.OpenSubKey(keyESRI, RegistryKeyPermissionCheck.ReadWriteSubTree);
          if (regKeyESRI == null)
            regKeyESRI = regKeySW.CreateSubKey(keyESRI, RegistryKeyPermissionCheck.ReadWriteSubTree);
          if (regKeyESRI == null)
            return true;

          RegistryKey regKeyDF = regKeyESRI.OpenSubKey(keyDF, RegistryKeyPermissionCheck.ReadWriteSubTree);
          if (regKeyDF == null)
            regKeyDF = regKeyESRI.CreateSubKey(keyDF, RegistryKeyPermissionCheck.ReadWriteSubTree);
          if (regKeyDF == null)
            return true;

          regKeyDF.SetValue(keyExecute, acceptValue);
        }
        catch { }
      }

      return license.Agreed;
    }

    private void OnOK(object sender, RoutedEventArgs e)
    {
      _agreed = true;
      Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
      _agreed = false;
      Close();
    }

  }
}
