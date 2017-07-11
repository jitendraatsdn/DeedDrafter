using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Tasks;
using ESRI.ArcGIS.Client.Symbols;

namespace DeedDrafter
{
  /// <summary>
  // Code behind for the identify window.
  // This dialog is create in MainWindow::MapPoint_MouseClick
  /// </summary>
  /// 
  public partial class IdentifyWindow : Window
  {
    private List<DataItem> _dataItems = null;

    string _activeIdentifyLayer;

    // List of just the layers. These correspond 1:1 w/ the layers in the combo box.
    List<string> _layersIdentified = new List<string>();    

    public IdentifyWindow()
    {
      InitializeComponent();
    }

    public void IdentifyPoint(Map ParcelMap, ref Configuration config, ESRI.ArcGIS.Client.Geometry.MapPoint clickPoint)
    {
      if (config.IdentifyURL == "")
        return;

      if (config.IdentifyLayerCount == 0)
        return;

      if (config.UseQueryIdentify)
      {
        _dataItems = new List<DataItem>();

        GeometryService geometryServicePointSnap = new GeometryService(config.GeometryServerUrl);
        if (geometryServicePointSnap == null)
          return;

        QueryItem queryItem = new QueryItem(ParcelMap, ref config, clickPoint, 0);

        geometryServicePointSnap.BufferCompleted += GeometryService_IdentifyPointBufferCompleted;
        geometryServicePointSnap.Failed += GeometryService_Failed;
        geometryServicePointSnap.CancelAsync();

        SimpleMarkerSymbol defaultSymbolMarker = new SimpleMarkerSymbol()
        {
          Color = System.Windows.Media.Brushes.Black, Size = 8,
          Style = SimpleMarkerSymbol.SimpleMarkerStyle.Circle
        };

        Graphic clickGraphic = new Graphic();
        clickGraphic.Symbol = defaultSymbolMarker as ESRI.ArcGIS.Client.Symbols.Symbol;
        clickGraphic.Geometry = clickPoint;

        // Input spatial reference for buffer operation defined by first feature of input geometry array
        clickGraphic.Geometry.SpatialReference = ParcelMap.SpatialReference;

        // If buffer spatial reference is GCS and unit is linear, geometry service will do geodesic buffering
        ESRI.ArcGIS.Client.Tasks.BufferParameters bufferParams = new ESRI.ArcGIS.Client.Tasks.BufferParameters()
        {
          BufferSpatialReference = ParcelMap.SpatialReference,
          OutSpatialReference = ParcelMap.SpatialReference,
          Unit = LinearUnit.Meter,
        };
        bufferParams.Distances.Add(config.SnapTolerance * config.SpatialReferenceUnitsPerMeter);
        bufferParams.Features.Add(clickGraphic);
        geometryServicePointSnap.BufferAsync(bufferParams, queryItem);
      }
      else
      {
        ESRI.ArcGIS.Client.Tasks.IdentifyParameters identifyParams = new IdentifyParameters()
        {
          Geometry = clickPoint,
          MapExtent = ParcelMap.Extent,
          Width = (int)ParcelMap.ActualWidth,
          Height = (int)ParcelMap.ActualHeight,
          LayerOption = LayerOption.visible,
          SpatialReference = ParcelMap.SpatialReference
        };

        // For performance, we allow certain layers to be only identified.
        if (config.IdentifyLayerIDs != null)
          foreach (int id in config.IdentifyLayerIDs)
            identifyParams.LayerIds.Add(id);

        IdentifyTask identifyTask = new IdentifyTask(config.IdentifyURL);
        identifyTask.ExecuteCompleted += IdentifyTask_ExecuteCompleted;
        identifyTask.Failed += IdentifyTask_Failed;

        QueryItem queryItem = new QueryItem(ParcelMap, ref config, clickPoint, 0);
        identifyTask.ExecuteAsync(identifyParams, queryItem);
      }
    }

    void GeometryService_IdentifyPointBufferCompleted(object sender, GraphicsEventArgs args)
    {
      QueryItem queryItem = args.UserState as QueryItem;
      queryItem.BufferedPoint = args.Results[0].Geometry;

      // We now have a result for out buffered start point. 
      // Issue a query to each identify layer.

      Query query = new ESRI.ArcGIS.Client.Tasks.Query();
      query.SpatialRelationship = SpatialRelationship.esriSpatialRelIntersects;
      query.Geometry = queryItem.BufferedPoint;
      query.OutFields.AddRange(new string[] { "*" });

      QueryTask queryTask = new QueryTask(queryItem.IdentifyLayerUrl);
      queryTask.ExecuteCompleted += QueryIdentifyTask_ExecuteCompleted;
      queryTask.Failed += QueryIdentifyTask_Failed;

      queryTask.ExecuteAsync(query, queryItem);
    }

    public void ShowFeatures(List<IdentifyResult> results)
    {
      _dataItems = new List<DataItem>();

      if (results != null && results.Count > 0)
      {
        int layerId = 0;
        int activeLayerId = 0;
        IdentifyComboBox.Items.Clear();
        _layersIdentified.Clear();
        foreach (IdentifyResult result in results)
        {
          Graphic feature = result.Feature;
          string title = result.Value.ToString() + " (" + result.LayerName + ")";

          feature.Attributes.Remove("Shape");
          feature.Attributes.Remove("OBJECTID");

          _dataItems.Add(new DataItem()
          {
            Title = title,
            Data = feature.Attributes
          });
          IdentifyComboBox.Items.Add(title);

          if (_activeIdentifyLayer == result.LayerName)
            activeLayerId = layerId;
          layerId++;
          _layersIdentified.Add(result.LayerName);
        }

        // Workaround for bug with ComboBox 
        IdentifyComboBox.UpdateLayout();

        IdentifyComboBox.SelectedIndex = activeLayerId;
      }

      Visibility = System.Windows.Visibility.Visible;
    }

    private void IdentifyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      int index = IdentifyComboBox.SelectedIndex;
      if (index > -1)
      {
        if (index < _dataItems.Count)
          IdentifyDetailsDataGrid.ItemsSource = _dataItems[index].Data;
        else
          IdentifyDetailsDataGrid.ItemsSource = null;

        if (index < _layersIdentified.Count)
          _activeIdentifyLayer = _layersIdentified[index];
        else
          _activeIdentifyLayer = "";

      }
    }

    private void IdentifyTask_ExecuteCompleted(object sender, IdentifyEventArgs args)
    {
      IdentifyDetailsDataGrid.ItemsSource = null;

      if (args.IdentifyResults != null && args.IdentifyResults.Count > 0)
      {
        IdentifyResultsPanel.Visibility = Visibility.Visible;
        NoResult.Visibility = Visibility.Collapsed;
        Results.Visibility = Visibility.Visible;

        ShowFeatures(args.IdentifyResults);
      }
      else
      {
        IdentifyComboBox.Items.Clear();
        IdentifyComboBox.UpdateLayout();

        Results.Visibility = Visibility.Collapsed;
        NoResult.Visibility = Visibility.Visible;
      }
    }

    public class DataItem
    {
      public string Title { get; set; }
      public IDictionary<string, object> Data { get; set; }
    }

    public class QueryItem
    {
      public QueryItem(Map ParcelMap, ref Configuration config, ESRI.ArcGIS.Client.Geometry.MapPoint clickPoint, int index)
      {
        // TODO: Complete member initialization
        _parcelMap = ParcelMap;
        _config = config;
        _clickPoint = clickPoint;
        _index = index;
      }

      Configuration _config = null;
      public Configuration Configuration()
      {
        return _config;
      }

      public bool Next()
      {
        if (_index >= _config.IdentifyLayerCount-1)
          return false;
        _index++;
        return true;
      }

      Map _parcelMap;
      public Map ParcelMap
      {
        get { return _parcelMap; }
      }

      public string Url
      {
        get { return _config.IdentifyURL; }
      }

      public string IdentifyLayerName
      {
        get { return _index < _config.IdentifyLayerCount ? _config.IdentifyLayerNames[_index] : ""; }
      }

      public string IdentifyLayerUrl
      {
        get { return _index < _config.IdentifyLayerCount ? _config.IdentifyLayerUrl[_index] : ""; }
      }

      ESRI.ArcGIS.Client.Geometry.Geometry _bufferedPoint = null;
      public ESRI.ArcGIS.Client.Geometry.Geometry BufferedPoint
      {
        get { return _bufferedPoint; }
        set { _bufferedPoint = value; }
      }

      ESRI.ArcGIS.Client.Geometry.MapPoint _clickPoint;
      public ESRI.ArcGIS.Client.Geometry.MapPoint ClickPoint
      {
        get { return _clickPoint; }
      }

      int _index = 0;
      public int Index
      {
        get { return _index; }
      }

      int _activeLayer = 0;
      public int ActiveLayer
      {
        get { return _activeLayer; }
        set { _activeLayer = value; }
      }

      public void UseQueryIdentify()
      {
        _config.UseQueryIdentify = true;
      }
    }

    void IdentifyTask_Failed(object sender, TaskFailedEventArgs args)
    {
      // Identify via identify service fails, then switch to using query service.
      QueryItem queryItem = args.UserState as QueryItem;
      if (queryItem != null)
      {
        queryItem.UseQueryIdentify();

        Configuration config = queryItem.Configuration();
        IdentifyPoint(queryItem.ParcelMap, ref config, queryItem.ClickPoint);
      }
      else
        MessageBox.Show((string)Application.Current.FindResource("strIdentifyServiceFailed") + ": " + args.Error);
    }

    /* ****** Identify using Query Service ****** */

    private void QueryIdentifyTask_ExecuteCompleted(object sender, QueryEventArgs args)
    {
      QueryItem queryItem = args.UserState as QueryItem;

      if (queryItem.Index == 0)
      {
        IdentifyComboBox.Items.Clear();
        _layersIdentified.Clear();
        IdentifyDetailsDataGrid.ItemsSource = null;
      }

      // display search results for this layers query.
      if (args.FeatureSet.Features.Count > 0)
      {
        foreach (Graphic feature in args.FeatureSet.Features)
        {
          string title = queryItem.IdentifyLayerName;

          feature.Attributes.Remove("Shape");
          feature.Attributes.Remove("OBJECTID");
          _dataItems.Add(new DataItem()
          {
            Title = title,
            Data = feature.Attributes
          });
          IdentifyComboBox.Items.Add(title);

          if (_activeIdentifyLayer == title)
            queryItem.ActiveLayer = _layersIdentified.Count;
          _layersIdentified.Add(title);
        }
      }

      if (queryItem.Next()) // this increments the internal index
      {
        Query query = new ESRI.ArcGIS.Client.Tasks.Query();
        query.SpatialRelationship = SpatialRelationship.esriSpatialRelIntersects;
        query.Geometry = queryItem.BufferedPoint;
        query.OutFields.AddRange(new string[] { "*" });

        QueryTask queryTask = new QueryTask(queryItem.IdentifyLayerUrl);
        queryTask.ExecuteCompleted += QueryIdentifyTask_ExecuteCompleted;
        queryTask.Failed += QueryIdentifyTask_Failed;

        queryTask.ExecuteAsync(query, queryItem);
      }
      else // We are done with all our queries, display the result.
      {
        IdentifyComboBox.SelectedIndex = queryItem.ActiveLayer;
        Visibility = System.Windows.Visibility.Visible;

        if ((_dataItems == null) || (_dataItems.Count == 0))
        {
          NoResult.Visibility = Visibility.Visible;
          Results.Visibility = Visibility.Collapsed;
        }
        else
        {
          NoResult.Visibility = Visibility.Collapsed;
          Results.Visibility = Visibility.Visible;
        }
      }
    }

    private void QueryIdentifyTask_Failed(object sender, TaskFailedEventArgs args)
    {
      MessageBox.Show((string)Application.Current.FindResource("strIdentifyServiceFailed") + ": " + args.Error);
    }

    private void GeometryService_Failed(object sender, TaskFailedEventArgs args)
    {
      MessageBox.Show((string)Application.Current.FindResource("strGeometryServiceFailed") + ": " + args.Error);
    }

  }
}
