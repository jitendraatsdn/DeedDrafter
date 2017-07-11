using System;
using System.Windows;
using System.Windows.Input;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Tasks;
using ESRI.ArcGIS.Client.Geometry;
using System.Collections.Generic;
using System.Windows.Media;

/* DeedDrafter revisions:
 * 
 * 10.2.0.0  Initial Release
 * 10.2.0.1  Correct line scaling when base map was not in meters or WebMercator (ie, in feet)
 *           Error 400, when operational lay does not contain OBJECTID field (ie, when it was fully qualified)
 *           Support vertical datum => for operational layer / unit support
 * 10.2.1.0  Upgraded RunTime to 10.2 (from RunTime 1.0, aka 3.1.0.473)
 *           Added new feature layer type which supports feature server layers
 *           Default the "type" property base on the URL. FeatureServer => feature, MapService => dynamic, ImageService => image. No default for tiled.
 *           Switched to accelerated display
 *           Implemented InfoWindow for onscreen parcel line entry (previous method didn't support accelerated display)
 *           Added misclose ratio to UI
 *           Added a progressor circle
 * 10.2.2.0  Upgraded RunTime to 10.2.2 
 *           Removed license requirement setup
 */

namespace DeedDrafter
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    double _defaultMapZoomFactor = 1.0;
    Configuration _xmlConfiguation;

    public MainWindow()
    {
      InitializeComponent();

      if (License.ShowWindow() != true)
      {
        Application.Current.Shutdown();
      }
      else if (!IsNet45OrNewer())
      {
        MessageBox.Show((string)Application.Current.FindResource("strDotNetVersionError"),
                        (string)Application.Current.FindResource("strTitle"));

        Application.Current.Shutdown();
      }
      else
      {
        string statusMessage;
        _xmlConfiguation = new Configuration();
        if (!_xmlConfiguation.ReadConfiguationFile("DeedDrafterConfiguration.xml", out statusMessage))
        {
          MessageBox.Show((string)Application.Current.FindResource("strConfigReadError") + "\n\n" + statusMessage, (string)Application.Current.FindResource("strTitle"));

          Application.Current.Shutdown();
        }
        else
        {
          ConfigureApplication(ref _xmlConfiguation);
          ResetGrid();
          _defaultMapZoomFactor = ParcelMap.ZoomFactor;
        }
      }
    }

    public static bool IsNet45OrNewer()
    {
      // Class "ReflectionContext" exists from .NET 4.5 onwards.
      return Type.GetType("System.Reflection.ReflectionContext", false) != null;
    }

    private void ConfigureApplication(ref Configuration xmlConfiguation)
    {
      Title = xmlConfiguation.Title;
      Width = xmlConfiguation.Width;
      Height = xmlConfiguation.Height;

      ParcelLines.MaxHeight = _xmlConfiguation.MaxGridHeight;

      // insert layer before [graphical] layers defined in xaml
      Int32 layerIndex = 0;
      String lastUnit = "";
      foreach (LayerDefinition definition in xmlConfiguation.DisplayLayers)
      {
        if (definition.Type == "dynamic")
        {
          ArcGISDynamicMapServiceLayer dynamicMS = new ArcGISDynamicMapServiceLayer();
          dynamicMS.Url = definition.Url;
          dynamicMS.ID = definition.Id;
          dynamicMS.InitializationFailed += Layer_InitializationFailed;
          ParcelMap.Layers.Insert(layerIndex++, dynamicMS);
          if ((dynamicMS.Units != null) && (dynamicMS.Units != "") && !xmlConfiguation.HasSpatialReferenceUnit)
            lastUnit = dynamicMS.Units;
        }

        if (definition.Type == "feature")
        {
          FeatureLayer featureMS = new FeatureLayer();
          featureMS.Url = definition.Url + "/" + definition.Id.ToString();
          featureMS.ID = definition.Id;
          featureMS.InitializationFailed += Layer_InitializationFailed;
          featureMS.Mode = FeatureLayer.QueryMode.OnDemand;
          ParcelMap.Layers.Insert(layerIndex++, featureMS);
          // FOOBAR FeatureLayer does not support unit?
        }

        if (definition.Type == "tiled")
        {
          ArcGISTiledMapServiceLayer tiledMS = new ArcGISTiledMapServiceLayer();
          tiledMS.Url = definition.Url;
          tiledMS.ID = definition.Id;
          tiledMS.InitializationFailed += Layer_InitializationFailed;
          ParcelMap.Layers.Insert(layerIndex++, tiledMS);
          if ((tiledMS.Units != null) && (tiledMS.Units != "") && !xmlConfiguation.HasSpatialReferenceUnit)
            lastUnit = tiledMS.Units;
        }

        if (definition.Type == "image")
        {
          ArcGISImageServiceLayer imageS = new ArcGISImageServiceLayer();
          imageS.Url = definition.Url;
          imageS.ID = definition.Id;
          imageS.InitializationFailed += Layer_InitializationFailed;
          ParcelMap.Layers.Insert(layerIndex++, imageS);
        }
      }

      if (!xmlConfiguation.HasSpatialReferenceUnit)
        xmlConfiguation.MapSpatialReferenceUnits = lastUnit;
     
      ESRI.ArcGIS.Client.Geometry.Envelope extent = null;
      if (xmlConfiguation.IsExtentSet())
        extent = new ESRI.ArcGIS.Client.Geometry.Envelope(xmlConfiguation.XMin, xmlConfiguation.YMin, xmlConfiguation.XMax, xmlConfiguation.YMax);
      else
        // Map will not zoom to, etc with out some value set.
        // Ideally we would like to set the extent to the full extent of the first
        // layer, but since they layer has hot been drawn yet null is returned.
        extent = new ESRI.ArcGIS.Client.Geometry.Envelope(100, 100, 100, 100);
      
      // if zero, the first inserted layer is used
      if ((xmlConfiguation.SpatialReferenceWKT != null) && (xmlConfiguation.SpatialReferenceWKT != ""))
        extent.SpatialReference = new ESRI.ArcGIS.Client.Geometry.SpatialReference(xmlConfiguation.SpatialReferenceWKT);
      else if (xmlConfiguation.SpatialReferenceWKID != 0)
        extent.SpatialReference = new ESRI.ArcGIS.Client.Geometry.SpatialReference(xmlConfiguation.SpatialReferenceWKID);

      ParcelMap.Extent = extent;
     
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      parcelData.Configuration = xmlConfiguation;

      QueryLabel.Text = xmlConfiguation.QueryLabel;
    }

    private void Layer_InitializationFailed(object sender, System.EventArgs e)
    {
      Layer layer = sender as Layer;
      if (layer.InitializationFailure != null)
        MessageBox.Show(layer.ID + ":" + layer.InitializationFailure.ToString());
    }

    IdentifyWindow _identifyDialog = null;
    private void MapPoint_MouseClick(object sender, ESRI.ArcGIS.Client.Map.MouseEventArgs e)
    {
      ParcelLineInfoWindow.IsOpen = false;

      if (DPE_ParcelEntry.IsExpanded || (!PDE_Tools.IsExpanded && !PDE_Find.IsExpanded && !PDE_Share.IsExpanded))
        ParcelTool(e.MapPoint);
      else if (PDE_Tools.IsExpanded)
      {
        if (_originPoint == null)
          ParcelTool(e.MapPoint);

        // Sometimes we don't get the mouse up event? Ensure its released now.
        _srPoint = null;
      }
      else if (PDE_Find.IsExpanded)
      {
        bool create = _identifyDialog == null || !_identifyDialog.IsLoaded;
        if (create)
          _identifyDialog = new IdentifyWindow();
        double dialogWidth = _identifyDialog.Width;    // If dialog is new, capture size
        double dialogHeight = _identifyDialog.Height;  // before its shown.
        if (create)
        {
          _identifyDialog.Owner = this;
          _identifyDialog.Show();
        }
        _identifyDialog.Visibility = System.Windows.Visibility.Hidden;
        _identifyDialog.IdentifyPoint(ParcelMap, ref _xmlConfiguation, e.MapPoint);

        int offset = 10; // Hard coded offset so we don't position the window exactly where the cursor is
        double top = e.ScreenPoint.Y + this.Top +
                      SystemParameters.ResizeFrameVerticalBorderWidth +
                      SystemParameters.CaptionHeight + offset;
        double left = e.ScreenPoint.X + this.Left +
                      SystemParameters.ResizeFrameVerticalBorderWidth + offset;

        // Keep the window in the virtual screen bounds
        double screenWidth = System.Windows.SystemParameters.VirtualScreenWidth;
        double screenHeight = System.Windows.SystemParameters.VirtualScreenHeight;
        if (left + dialogWidth > screenWidth)
          left = screenWidth - dialogWidth;
        if (top + dialogHeight > screenHeight)
          top = screenHeight - dialogHeight;

        _identifyDialog.Top = top;
        _identifyDialog.Left = left;
      }
    }

    private void GeometryService_Failed(object sender, TaskFailedEventArgs args)
    {
      MessageBox.Show((string)Application.Current.FindResource("strGeometryServiceFailed") + ": " + args.Error, (string)Application.Current.FindResource("strTitle"));
      CancelScaleRotate();
    }

    private void QueryTask_Failed(object sender, TaskFailedEventArgs args)
    {
      MessageBox.Show((string)Application.Current.FindResource("strQueryServiceSupport") + "\n\n" + args.Error, (string)Application.Current.FindResource("strQueryServiceFailed"));
      CancelScaleRotate();
    }

    private void ParcelMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
      ScaleRotate(e.GetPosition(this));
    }

    private void ParcelMap_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
      CancelScaleRotate();

      if (IsParcelEntryMode())
        ParcelMap_KeyUp(sender, null);
    }

    private void RotationScale_Click(object sender, RoutedEventArgs e)
    {
      CancelScaleRotate();
    }

    private void Information_MouseDown(object sender, MouseButtonEventArgs e)
    {
      string helpFile = "Help.pdf";
      if (System.IO.File.Exists(helpFile))
        System.Diagnostics.Process.Start(helpFile);
      else
        MessageBox.Show((string)Application.Current.FindResource("strHelpFileMissing"), (string)Application.Current.FindResource("strTitle"));

      // System.Diagnostics.Process.Start(@"http://esriurl.com/DeedDrafter");
    }

    private void ParcelMap_ExtentChanged(object sender, ExtentEventArgs e)
    {
      if (System.Diagnostics.Debugger.IsAttached)
        ShowProjectedExtent(sender as Map);

      // If we have zoomed less than the min resolution, zoom out a slight amount of the min to ensure display is drawn
      double minRes = ParcelMap.MinimumResolution * 1.0001;
      if (ParcelMap.Resolution <= minRes)
        ParcelMap.ZoomToResolution(minRes);
    }

    private void ShowProjectedExtent(Map thisMap)
    {
      if (thisMap != null)
        System.Console.WriteLine("Current extent {0}", thisMap.Extent);

      if (_xmlConfiguation.OutputSpatialReference == null || thisMap == null)
          return;

      // if we are in Web Mercator, project the extent back into the output spatial reference.
      //
      // This is for debugging only.

      GeometryService geometryServiceProject = new GeometryService(_xmlConfiguation.GeometryServerUrl);
      if (geometryServiceProject == null)
        return;

      geometryServiceProject.ProjectCompleted += GeometryService_CalculateOutputCoords;
      geometryServiceProject.Failed += GeometryService_FailedWebMercatorScale;
      geometryServiceProject.CancelAsync();

      var graphicList = new List<Graphic>();

      double x = thisMap.Extent.XMin;
      double y = thisMap.Extent.YMin;
      MapPoint minPoint = new MapPoint(x, y, ParcelMap.SpatialReference);
      Graphic minGraphic = new Graphic();
      minGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      minGraphic.Geometry = minPoint;
      graphicList.Add(minGraphic);

      x = thisMap.Extent.XMax;
      y = thisMap.Extent.YMax;
      MapPoint maxPoint = new MapPoint(x, y, ParcelMap.SpatialReference);
      Graphic maxGraphic = new Graphic();
      maxGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      maxGraphic.Geometry = maxPoint;
      graphicList.Add(maxGraphic);

      if (_xmlConfiguation.HasDatumTransformation)
      {
        DatumTransform transformation = new DatumTransform();
        if (_xmlConfiguation.DatumTransformationWKID > 0)
          transformation.WKID = _xmlConfiguation.DatumTransformationWKID;
        else
          transformation.WKT = _xmlConfiguation.DatumTransformationWKT;


        //geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference,
        //transformation, _xmlConfiguation.DatumTransformationForward);
        geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference,
        transformation);
         
      }
      else
        geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference);
    }

    void GeometryService_CalculateOutputCoords(object sender, GraphicsEventArgs args)
    {
      if (args == null || args.Results == null || args.Results.Count != 2)
        return; // should not occur

      MapPoint minPoint = (MapPoint)args.Results[0].Geometry;
      MapPoint maxPoint = (MapPoint)args.Results[1].Geometry;

      System.Console.WriteLine("Projected extent Min:{0},{1} Max:{2},{3}", minPoint.X, minPoint.Y, maxPoint.X, maxPoint.Y);
    }

    private void ParcelMap_Progress(object sender, ProgressEventArgs e)
    {
      if (e == null || sender == null)
        DFProgressor.Visibility = System.Windows.Visibility.Hidden;
      else 
        DFProgressor.Visibility = e.Progress == 100 ? System.Windows.Visibility.Hidden : System.Windows.Visibility.Visible;
    }
  }
}

