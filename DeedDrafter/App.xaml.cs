using ESRI.ArcGIS;
using ESRI.ArcGIS.Client;
using Esri.ArcGISRuntime;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DeedDrafter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
      private void Application_Startup(object sender, StartupEventArgs e)
      {
        try
        {
            ArcGISRuntimeEnvironment.Initialize();
            
        }
        catch (Exception ex)
        {
          MessageBox.Show(ex.ToString(), (string)Application.Current.FindResource("strTitle"));

          // Exit application
          this.Shutdown();
        }
      }
    }
}
