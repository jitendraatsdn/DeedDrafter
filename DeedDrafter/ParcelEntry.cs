using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Symbols;
using ESRI.ArcGIS.Client.Tasks;

using Utilities;
using PointDictionary = System.Collections.Generic.Dictionary<System.Int32, ESRI.ArcGIS.Client.Geometry.MapPoint>;

using System.Diagnostics;
using ESRI.ArcGIS.Client.Geometry;

namespace DeedDrafter
{
  /// <summary>
  // This primary code behind for the parcel entry grid.
  /// </summary>
  /// 
  public partial class MainWindow : Window
  {
    ESRI.ArcGIS.Client.Geometry.MapPoint _originPoint;
    PointDictionary _calculatedPoints = new PointDictionary();
    Dictionary<Int32, bool> _centerPoints = new Dictionary<Int32, bool>();

    Int32 _lastRowCommited = -1;
    bool _moveToBearingColumn = false;

    private void DPE_ParcelEntry_Expanded(object sender, RoutedEventArgs e)
    {
      VerifyAndFixUnit();  // force UnitsPerMeter to update
    }

    private void ParcelTool(ESRI.ArcGIS.Client.Geometry.MapPoint clickPoint)
    {
      // This routine sets up the initial point location of the parcel.
      // It gets the XY location of the point, and buffers the point and 
      // issues a query to find a point to snap tool.

      _originPoint = clickPoint;

      GeometryService geometryServicePointSnap = new GeometryService(_xmlConfiguation.GeometryServerUrl);
      if (geometryServicePointSnap == null) 
      {
        CalculateAndAddLineGraphics();
        return;
      }
      geometryServicePointSnap.BufferCompleted += GeometryService_PointBufferCompleted;
      geometryServicePointSnap.Failed += GeometryService_Failed;
      geometryServicePointSnap.CancelAsync();

      VerifyAndFixUnit();  // force UnitsPerMeter to update
      
      Graphic clickGraphic = new Graphic();
      clickGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      clickGraphic.Geometry = _originPoint;
      
      // Input spatial reference for buffer operation defined by first feature of input geometry array
      clickGraphic.Geometry.SpatialReference = ParcelMap.SpatialReference;

      // If buffer spatial reference is GCS and unit is linear, geometry service will do geodesic buffering
      ESRI.ArcGIS.Client.Tasks.BufferParameters bufferParams = new ESRI.ArcGIS.Client.Tasks.BufferParameters()
      {
        BufferSpatialReference = ParcelMap.SpatialReference,
        OutSpatialReference = ParcelMap.SpatialReference,
        Unit = LinearUnit.Meter,
      };
      bufferParams.Distances.Add(_xmlConfiguation.SnapTolerance);
      bufferParams.Features.Add(clickGraphic);
      geometryServicePointSnap.BufferAsync(bufferParams);

      // For perceived performance, draw what we have and redraw once the service returns.
      // If we are Web Mercator, just draw the start point if that's all we have and the 
      // scale has never been calculated. Otherwise we need to wait for the service to 
      // return, as we don't want the user to see scaling is occurring.
      if (IsWebMercator() && _xmlConfiguation.WebMercatorScale == 1)
      {
        if (_calculatedPoints.Count == 0)
          CalculateAndAddLineGraphics(true);
      }
      else
        CalculateAndAddLineGraphics();
    }

    void ResetGrid()
    {
      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
      if (parcelRecordData != null)
      {
        parcelRecordData.Clear();
        parcelRecordData.Add(new ParcelLineRow(ref _xmlConfiguation) { From = "1" });
      }
      ResetRotationScale();
    }

    class QueryResultContainer
    {
      public QueryResultContainer(ref List<QueryResult> queries, Int32 index)
      {
        _queries = queries;
        _index = index;
        _count = queries.Count;
      }

      Int32 _index;
      public QueryResult Query()
      {
        return _queries[_index];
      }

      List<QueryResult> _queries;
      public List<QueryResult> Queries
      {
        get { return _queries; }
      }

      int _count;
      public int Count() 
      {
        return _count;
      } 
    }

    class QueryResult
    {
      private QueryResult()
      {
      }

      public QueryResult(ESRI.ArcGIS.Client.Geometry.MapPoint searchPoint, ESRI.ArcGIS.Client.Geometry.Polygon bufferGraphic, double searchDistance, string layer)
      {
        if (searchPoint == null)
          throw new Exception("Missing search point");
        SearchPoint = searchPoint;
        BufferGraphic = bufferGraphic;
        _layer = layer;

        _closestDistance = searchDistance;
      }

      string _layer;
      public string Layer
      {
        get { return _layer; }
      }

      private ESRI.ArcGIS.Client.Geometry.MapPoint _searchPoint;
      private ESRI.ArcGIS.Client.Geometry.MapPoint _closestPoint = null;
      private ESRI.ArcGIS.Client.Geometry.Polygon _bufferGraphic;
      private double _closestDistance = double.MaxValue;
      private bool _resultFound = false;

      public ESRI.ArcGIS.Client.Geometry.Polygon BufferGraphic
      {
        get { return _bufferGraphic; }
        set { _bufferGraphic = value; }
      }

      public ESRI.ArcGIS.Client.Geometry.MapPoint SearchPoint
      {
        get { return _searchPoint; }
        set { _searchPoint = value; }
      }

      public void SetResult(ESRI.ArcGIS.Client.Geometry.MapPoint closestPoint, double distance)
      {
        _closestPoint = closestPoint;
        _closestDistance = distance;
        _resultFound = true;
      }

      public ESRI.ArcGIS.Client.Geometry.MapPoint ClosestPoint
      {
        get { return _closestPoint; }
      }

      public double ClosestDistance
      {
        get { return _closestDistance; }
      }

      public bool ResultFound
      {
        get { return _resultFound; }
        set { _resultFound = value; }
      }
    };

    Int32 _queryPointCompleted;
    void GeometryService_PointBufferCompleted(object sender, GraphicsEventArgs args)
    {
      // We now have a result for out buffered start point. 
      // Issue a query to each snap layer to find the closest point.

      Graphic bufferGraphic = new Graphic();
      bufferGraphic.Geometry = args.Results[0].Geometry;
      bufferGraphic.Symbol = LayoutRoot.Resources["BufferSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      bufferGraphic.SetZIndex(1);

      ESRI.ArcGIS.Client.Geometry.Polygon buffer = bufferGraphic.Geometry as ESRI.ArcGIS.Client.Geometry.Polygon;

      List<QueryResult> queryResults = new List<QueryResult>();
      foreach (LayerDefinition definition in _xmlConfiguation.SnapLayers)
        queryResults.Add(new QueryResult(_originPoint, buffer, _xmlConfiguation.SnapTolerance, definition.Layer()));

      _queryPointCompleted = 0;
      bool requestSent = false;
      for (Int32 i = 0; i < queryResults.Count; i++)
        if (ExecutePointQuery(new QueryResultContainer(ref queryResults, i)))
          requestSent = true;

      if (!requestSent)
      {
        bool isWebMercator = CalculateWebMercatorScale();
      }
    }

    bool ExecutePointQuery(QueryResultContainer queryResultContainer)
    {
      QueryResult queryResult = queryResultContainer.Query();
      QueryTask queryTaskPoint = new QueryTask(queryResult.Layer);
      if (queryTaskPoint == null)
        return false;

      queryTaskPoint.ExecuteCompleted += QueryTask_PointExecuteCompleted;
      queryTaskPoint.Failed += QueryTask_Failed;
      queryTaskPoint.CancelAsync();

      ESRI.ArcGIS.Client.Tasks.Query query = new ESRI.ArcGIS.Client.Tasks.Query();
      query.ReturnGeometry = true;
      query.OutSpatialReference = ParcelMap.SpatialReference;
      query.Geometry = queryResult.BufferGraphic;

      queryTaskPoint.ExecuteAsync(query, queryResultContainer);

      return true;
    }

    private void QueryTask_PointExecuteCompleted(object sender, QueryEventArgs args)
    {
      QueryResultContainer queryResultContainer = args.UserState as QueryResultContainer;
      if (queryResultContainer == null)
        return;
      QueryResult queryResult = queryResultContainer.Query();

      if (args.FeatureSet.Features.Count > 0)
      {
        // Find the closest point and use that at the origin point.
        // if no points are found (that should not be the case)
        // then the clicked point is used.
        double x = queryResult.SearchPoint.X;
        double y = queryResult.SearchPoint.Y;
        foreach (Graphic feature in args.FeatureSet.Features)
        {
          // test type of feature, and test it's end points.

          if (feature.Geometry is ESRI.ArcGIS.Client.Geometry.Polygon)
          {
            ESRI.ArcGIS.Client.Geometry.Polygon featurePolygon = feature.Geometry as ESRI.ArcGIS.Client.Geometry.Polygon;
            foreach (ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection in featurePolygon.Rings)
              foreach (ESRI.ArcGIS.Client.Geometry.MapPoint featurePoint in pointCollection)
              {
                double distance = GeometryUtil.LineLength(x, y, featurePoint);
                if (distance < queryResult.ClosestDistance)
                  queryResult.SetResult(featurePoint, distance);
              }
          }
          else if (feature.Geometry is ESRI.ArcGIS.Client.Geometry.Polyline)
          {
            ESRI.ArcGIS.Client.Geometry.Polyline featurePolyline = feature.Geometry as ESRI.ArcGIS.Client.Geometry.Polyline;
            foreach (ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection in featurePolyline.Paths)
              foreach (ESRI.ArcGIS.Client.Geometry.MapPoint featurePoint in pointCollection)
              {
                double distance = GeometryUtil.LineLength(x, y, featurePoint);
                if (distance < queryResult.ClosestDistance)
                  queryResult.SetResult(featurePoint, distance);
              }
          }
          else if (feature.Geometry is ESRI.ArcGIS.Client.Geometry.MapPoint)
          {
            ESRI.ArcGIS.Client.Geometry.MapPoint featurePoint = feature.Geometry as ESRI.ArcGIS.Client.Geometry.MapPoint;
            double distance = GeometryUtil.LineLength(x, y, featurePoint);
            if (distance < queryResult.ClosestDistance)
              queryResult.SetResult(featurePoint, distance);
          }
        }
      }

      // If we are the last query to execute, then get the best of all the results.

      System.Threading.Interlocked.Increment(ref _queryPointCompleted);
      if (_queryPointCompleted == queryResultContainer.Count())
      {
        double closestDistance = double.MaxValue;
        foreach (QueryResult queryResult2 in queryResultContainer.Queries)
          if (queryResult2.ResultFound && (queryResult2.ClosestDistance < closestDistance))
          {
            closestDistance = queryResult2.ClosestDistance;
            _originPoint = queryResult2.ClosestPoint;
          }

        bool redraw = (closestDistance != double.MaxValue);  // Is there a result?

        // Calculate scale for Web Mercator to account for different datum & units
        bool isWebMercator = CalculateWebMercatorScale();
        // if we already have a WebMercatorScale, lets assume thats ok to begin with, and we will
        // get a correction when the services returns (speed things up).
        if (!isWebMercator && redraw || _xmlConfiguation.WebMercatorScale != 1)
          CalculateAndAddLineGraphics();
      }
    }

    // If our map is a hosted web service in Web Mercator and our destination 
    // is a standard planer SR, we have no choice but to workout a scale difference
    // and apply that to the calculated points. This is not a perfect solution, but
    // it yields satisfactory results (ie, we are going across different datum).
    // Minor joining corrections will need to be done when the parcel is joined to the fabric. 
    //
    // Solution:
    //  - Project the start point to the planer SR
    //  - Create an offset point (250m) in the positive Y direction
    //  - Project the original point and offset point back to Web Mercator
    //  - Calculate distance of two points
    //  - Calculate scale based on new distance and the original offset of 250.
    //
    bool IsWebMercator()
    {
      return ParcelMap.SpatialReference.WKID == 3857 || ParcelMap.SpatialReference.WKID == 102100;
    }

    bool CalculateWebMercatorScale()
    {
      if (!IsWebMercator())
        return false;

      if (_xmlConfiguation.OutputSpatialReference == null)
        return false;

      GeometryService geometryServiceProject = new GeometryService(_xmlConfiguation.GeometryServerUrl);
      if (geometryServiceProject == null)
        return false;

      geometryServiceProject.ProjectCompleted += GeometryService_CalculateWebMercatorScale;
      geometryServiceProject.Failed += GeometryService_FailedWebMercatorScale;
      geometryServiceProject.CancelAsync();

      double x = _originPoint.X;
      double y = _originPoint.Y;
      MapPoint startPoint = new MapPoint(x, y, ParcelMap.SpatialReference);

      Graphic startGraphic = new Graphic();
      startGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      startGraphic.Geometry = startPoint;

      var graphicList = new List<Graphic>();
      graphicList.Add(startGraphic);

      if (_xmlConfiguation.HasDatumTransformation)
      {
          
        DatumTransform transformation = new DatumTransform();
        if (_xmlConfiguation.DatumTransformationWKID > 0)
          transformation.WKID = _xmlConfiguation.DatumTransformationWKID;
        else
          transformation.WKT = _xmlConfiguation.DatumTransformationWKT;

        //geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference,
        //  transformation, _xmlConfiguation.DatumTransformationForward);
        geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference,
     transformation);
      }
      else
        geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference);

      return true;
    }

    void GeometryService_CalculateWebMercatorScale(object sender, GraphicsEventArgs args)
    {
      if (args == null || args.Results == null || args.Results.Count != 1)
        return; // should not occur

      MapPoint pointA = (MapPoint)args.Results[0].Geometry;

      GeometryService geometryServiceProject = new GeometryService(_xmlConfiguation.GeometryServerUrl);
      if (geometryServiceProject == null)
        return; // should not occur (already tested)
      if (_xmlConfiguation.OutputSpatialReference == null)
        return; // should not occur (already tested)

      geometryServiceProject.ProjectCompleted += GeometryService_CalculateWebMercatorScale2;
      geometryServiceProject.Failed += GeometryService_FailedWebMercatorScale;
      geometryServiceProject.CancelAsync();

      double testLength = 250 / _xmlConfiguation.SpatialReferenceUnitsPerMeter;
      // Calculate an off set point 250m positive
      double x = pointA.X;
      double y = pointA.Y + testLength;
      MapPoint offsetPoint = new MapPoint(x, y, _xmlConfiguation.OutputSpatialReference);

      Graphic originGraphic = new Graphic();
      originGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      originGraphic.Geometry = pointA;

      Graphic offsetGraphic = new Graphic();
      offsetGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      offsetGraphic.Geometry = offsetPoint;

      var graphicList = new List<Graphic>();
      graphicList.Add(originGraphic);
      graphicList.Add(offsetGraphic);

      if (_xmlConfiguation.HasDatumTransformation)
      {
        DatumTransform transformation = new DatumTransform();
        if (_xmlConfiguation.DatumTransformationWKID > 0)
          transformation.WKID = _xmlConfiguation.DatumTransformationWKID;
        else
          transformation.WKT = _xmlConfiguation.DatumTransformationWKT;

        //geometryServiceProject.ProjectAsync(graphicList, ParcelMap.SpatialReference,
        //  transformation, !_xmlConfiguation.DatumTransformationForward, testLength);
        geometryServiceProject.ProjectAsync(graphicList, ParcelMap.SpatialReference,
transformation);
      }
      else
        geometryServiceProject.ProjectAsync(graphicList, ParcelMap.SpatialReference, testLength);
    }

    static bool _showSRError = true;
    void GeometryService_CalculateWebMercatorScale2(object sender, GraphicsEventArgs args)
    {
      if (args == null || args.Results == null || args.Results.Count != 2)
        return; // should not occur

      MapPoint pointA = (MapPoint)args.Results[0].Geometry;
      MapPoint pointB = (MapPoint)args.Results[1].Geometry;

      // If the SR in the configuration file is formatted wrongly, we may ended up with a GCS SR. 
      // This will produce an error, since we add 250 the the Y
      if (_showSRError && double.IsNaN(pointB.X))
      {
        MessageBox.Show((string)Application.Current.FindResource("strSpatialReferenceError"), (string)Application.Current.FindResource("strTitle"));
        _showSRError = false;
      }

      double scale = (GeometryUtil.LineLength(pointA, pointB) / (double)args.UserState) /
                                          _xmlConfiguation.SpatialReferenceUnitsPerMeter;

      Debug.Assert(!double.IsNaN(scale));
      _xmlConfiguation.WebMercatorScale = double.IsNaN(scale) ? 1.0 : scale;

      CalculateAndAddLineGraphics();
    }

    private void GeometryService_FailedWebMercatorScale(object sender, TaskFailedEventArgs args)
    {
      CalculateAndAddLineGraphics();
      GeometryService_Failed(sender, args);
    }

    #region Draw and calculate sketch
    private void AddGraphicPoint(ref GraphicsLayer graphicsLayer, ref ESRI.ArcGIS.Client.Geometry.MapPoint point, bool snapPoint)
    {
      string resource;
      if (snapPoint)
      {
        if (_snapObjects.Count > 0)
          resource = "BlueMarkerSymbolDiamond";   // For result found
        else
          resource = "BlueMarkerSymbolSquare";    // No result; no snap point used / found.
      }
      else
        resource = "RedMarkerSymbol";

      ESRI.ArcGIS.Client.Graphic graphic = new ESRI.ArcGIS.Client.Graphic()
      {
        Geometry = point,
        Symbol = LayoutRoot.Resources[resource] as ESRI.ArcGIS.Client.Symbols.Symbol
      };
      graphicsLayer.Graphics.Add(graphic);
    }

    private void AddGraphicCenterPoint(ref GraphicsLayer graphicsLayer, ref ESRI.ArcGIS.Client.Geometry.MapPoint point)
    {
      // Draw 8x8 black square
      ESRI.ArcGIS.Client.Graphic graphic2 = new ESRI.ArcGIS.Client.Graphic()
      {
        Geometry = point,
        Symbol = LayoutRoot.Resources["HollowMarkerSymbol2"] as ESRI.ArcGIS.Client.Symbols.Symbol
      };
      graphicsLayer.Graphics.Add(graphic2);

      // Draw 4x4 white square
      ESRI.ArcGIS.Client.Graphic graphic1 = new ESRI.ArcGIS.Client.Graphic()
      {
        Geometry = point,
        Symbol = LayoutRoot.Resources["HollowMarkerSymbol1"] as ESRI.ArcGIS.Client.Symbols.Symbol
      };
      graphicsLayer.Graphics.Add(graphic1);
    }

    private void AddGraphicLine(ParcelLineRow lineRow, double bearing, ref GraphicsLayer graphicsLayer, ref ESRI.ArcGIS.Client.Geometry.Polyline line, Int32 gridIndex)
    {
      // Add line

      ESRI.ArcGIS.Client.Graphic graphic = new Graphic();
      graphic.Geometry = line;
      graphic.Attributes.Add("GridIndex", gridIndex);

      if (lineRow.Category == LineCategory.OriginConnection)
        graphic.Symbol = LayoutRoot.Resources["OriginConnectionLineSymbol"] as Symbol;
      else
        graphic.Symbol = LayoutRoot.Resources["BoundaryLineSymbol"] as Symbol;

      graphicsLayer.Graphics.Add(graphic);

      if ((lineRow.Bearing == "") || (lineRow.Bearing == null))
        return;

      string bearingStr;
      if (lineRow.Bearing[0] == '*')
        bearingStr = lineRow.Bearing.Substring(1);
      else
        bearingStr = lineRow.Bearing;

      // Add text to graphic line
      //
      // Currently we have no way to scale the text or make it scale dependent 
      // (if it was on the server, it was be easy). For, lets not draw text.
      //
      //ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection = line.Paths[0];
      //ESRI.ArcGIS.Client.Geometry.MapPoint plPoint1 = pointCollection[0];
      //ESRI.ArcGIS.Client.Geometry.MapPoint plPoint2 = pointCollection[1];
      //
      //// Get the mid point of the line for text
      //Point pt1 = new Point(plPoint1.X, plPoint1.Y);
      //Point pt2 = new Point(plPoint2.X, plPoint2.Y);
      //Point midPoint = new Point((pt1.X + pt2.X) / 2, (pt1.Y + pt2.Y) / 2);
      //ESRI.ArcGIS.Client.Geometry.MapPoint plPoint3 = new ESRI.ArcGIS.Client.Geometry.MapPoint();
      //plPoint3.X = midPoint.X;
      //plPoint3.Y = midPoint.Y;
      //
      //// if over 180, flip angle and point over, so its the right way up.
      //double angle = GeometryUtil.RadianToDegree(bearing);
      //if (angle > 180)
      //{
      //  angle -= 180;
      //  plPoint3 = plPoint2;
      //}
      //else
      //  plPoint3 = plPoint1;
      //
      //ESRI.ArcGIS.Client.Graphic textGraphic = new Graphic();
      //textGraphic.Geometry = plPoint3;
      //textGraphic.Symbol = LayoutRoot.Resources["GraphicTextSymbol"] as Symbol;
      //
      //if ((lineRow.Radius == "") || (lineRow.Radius == null))
      //  textGraphic.Attributes.Add("Content", bearingStr + " " + lineRow.Distance);
      //else if ((lineRow.Distance == "") || (lineRow.Distance == null))
      //  textGraphic.Attributes.Add("Content", bearingStr + " " + lineRow.Parameter2 + " R:" + lineRow.Radius);
      //else
      //  textGraphic.Attributes.Add("Content", bearingStr + " " + lineRow.Distance + " R:" + lineRow.Radius);
      //
      //textGraphic.Attributes.Add("Heading", (angle - 90).ToString("F2"));
      //graphicsLayer.Graphics.Add(textGraphic);
    }

    bool IsParcelEntryMode()
    {
      return !(PDE_Share.IsExpanded || PDE_Tools.IsExpanded || PDE_Find.IsExpanded);
    }

    #region In-map parcel line entry
    private void LineGraphicsLayer_MouseEnter(object sender, GraphicMouseEventArgs e)
    {
      // only show "quick" line change dialog if Parcel entry mode is open, 
      // or all other modes are closed.
      if (!IsParcelEntryMode())
      {
        ParcelLineInfoWindow.IsOpen = false; 
        return;
      }

      Point pointLoc = e.GetPosition(this);
      ESRI.ArcGIS.Client.Geometry.MapPoint loc = ParcelMap.ScreenToMap(pointLoc);

      GraphicsLayer graphicLayer = sender as GraphicsLayer;

      IEnumerable<Graphic> selected = graphicLayer.FindGraphicsInHostCoordinates(pointLoc);
      if (selected == null)
        return;
      Graphic graphic = selected.FirstOrDefault();
      if (graphic == null)
        return;

      object gridIndexObj;
      if (!graphic.Attributes.TryGetValue("GridIndex", out gridIndexObj))
        return;
      Int32 gridIndex = (Int32)gridIndexObj;

      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
      if (gridIndex > parcelRecordData.Count - 1)
        return;

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      parcelData.SelectedRow = gridIndex;

      ParcelLineInfoWindow.Anchor = loc;   
      ParcelLineInfoWindow.IsOpen = true;

      // Since a ContentTemplate is defined, Content will define the DataContext for the ContentTemplate
      ParcelLineInfoWindow.Content = ParcelGridContainer.DataContext;

      SetFocusToFirstPopupCell(false);
    }

    private void MapEdit_LostFocus(object sender, RoutedEventArgs e)
    {
      TextBox textBox = sender as TextBox;
      BindingExpression bindingExp = textBox.GetBindingExpression(TextBox.TextProperty);
      bindingExp.UpdateSource();

      CalculateAndAddLineGraphics();           
    }

    private void MapEdit_KeyUp(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        UIElement uiElement = e.OriginalSource as UIElement;
        uiElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

        CalculateAndAddLineGraphics();
      }
    }

    private void MapEdit_KeyUpChord(object sender, KeyEventArgs e)
    {
      if (ParcelLineInfoWindow.IsOpen &&
          ParcelLineInfoWindow.Visibility == System.Windows.Visibility.Visible && 
          e.Key == Key.Enter)
      {
        ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
        if (parcelData.IsLastRowSelected())
        {
          // Code to close popup edit dialog
          //
          // ParcelLineInfoWindow.IsOpen = false;
          // ParcelMap.Focus();  // Allow ParcelLineInfoWindow_MouseEnter to receive event to reopen

          SetFocusToFirstPopupCell(true);  // commit row (not required if closing popup)

          // Add a new row to grid
          ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
          parcelData.SelectedRow = parcelRecordData.Count;
        }
        else
          SetFocusToFirstPopupCell(false);

        CalculateAndAddLineGraphics();
      }
    }

    private void ParcelLineInfoWindow_MouseEnter(object sender, MouseEventArgs e)
    {
      ParcelMap.ZoomFactor = 1.0; // Turn off any zooming when using onscreen entry.
    }

    private void ParcelLineInfoWindow_MouseLeave(object sender, MouseEventArgs e)
    {
      ParcelMap.ZoomFactor = _defaultMapZoomFactor;
    }

    // show quick line edit control
    private void ParcelMap_KeyUp(object sender, KeyEventArgs e)
    {
      if (ParcelLineInfoWindow.IsOpen && 
          ParcelLineInfoWindow.Visibility == System.Windows.Visibility.Visible)
        return; // We don't want to show any new dialog when editing parcel lines

      if ((e != null) && (e.Key != Key.Enter))
        return; // Only show new 

      Point pointLoc = Mouse.GetPosition(this);
      ESRI.ArcGIS.Client.Geometry.MapPoint loc = ParcelMap.ScreenToMap(pointLoc);
      ESRI.ArcGIS.Client.Geometry.Envelope envelope = new ESRI.ArcGIS.Client.Geometry.Envelope(loc.X, loc.Y, loc.X, loc.Y);

      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
      Int32 gridIndex = parcelRecordData.Count;

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      parcelData.SelectedRow = gridIndex;

      ParcelLineInfoWindow.Content = ParcelGridContainer.DataContext;
      ParcelLineInfoWindow.Anchor = loc; 
      ParcelLineInfoWindow.IsOpen = true;

      SetFocusToFirstPopupCell(true);
    }

    private void SetFocusToFirstPopupCell(bool selectCell)
    {
      var firstFocusable = FindFirstFocusableElement(ParcelLineInfoWindow);
      if (firstFocusable == null)
        return;

      firstFocusable.Focus();
      if (!selectCell)
        return;

      var textbox = firstFocusable as TextBox;
      if (textbox != null)
        textbox.SelectAll();
    }

    private IInputElement FindFirstFocusableElement(DependencyObject obj)
    {
      IInputElement firstFocusable = null;

      int count = VisualTreeHelper.GetChildrenCount(obj);
      for (int i = 0; i < count && null == firstFocusable; i++)
      {
        DependencyObject child = VisualTreeHelper.GetChild(obj, i);
        IInputElement inputElement = child as IInputElement;
        if (null != inputElement && inputElement.Focusable)
        {
          firstFocusable = inputElement;
        }
        else
        {
          firstFocusable = FindFirstFocusableElement(child);
        }
      }

      return firstFocusable;
    }

    private void ParcelLineInfoWindow_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      ParcelLineInfoWindow.IsOpen = false;
    }

    #endregion

    // All lines in the grid have from/to point ids. 
    // Since we a developing a grid without from/to ids in the grid
    // we need to re-sequence the ids each time, since the user can't 
    // correct/edit them.

    private void ResequenceLineIds(bool forceLastPoint)
    {
      Int32 id = 1;
      Int32 index = 0;

      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
      foreach (ParcelLineRow line in parcelRecordData)
      {
        index++;
        bool lastRow = parcelRecordData.Count == index;

        if (line.GetChordDistance() == 0.0)
          continue;

        line.From = id.ToString();
        id++;

        if (line.GetRadius() != 0)
        {
          line.CenterPoint = id;
          id++;
        }
        else
          line.CenterPoint = null;

        line.To = id.ToString();
      }
    }

    // This routine calculates points, and creates line graphics.
    // It's the work horse the the data entry grid.

    private void CalculateAndAddLineGraphics(bool startPointOnly = false)   
    {
      if (_originPoint == null)  // We need to have an origin point placed down
        return;                  // before we start this routine.

      ResequenceLineIds(false);

      GraphicsLayer pointGraphicsLayer = ParcelMap.Layers["SketchPointGraphicLayer"] as GraphicsLayer;
      pointGraphicsLayer.ClearGraphics();
      GraphicsLayer lineGraphicsLayer = ParcelMap.Layers["SketchLineGraphicLayer"] as GraphicsLayer;
      lineGraphicsLayer.ClearGraphics();

      AddGraphicPoint(ref pointGraphicsLayer, ref _originPoint, false);

      if (startPointOnly && _calculatedPoints.Count() == 0)
        return;

      _calculatedPoints.Clear();
      _centerPoints.Clear();

      ESRI.ArcGIS.Client.Geometry.MapPoint startBoundaryPoint = null;
      ESRI.ArcGIS.Client.Geometry.MapPoint stopBoundaryPoint = null;
      Int32 startBoundaryPointId = -1;
      Int32 stopBoundaryPointId = -1;
      Int32 lastBoundaryPointId = -1;

      ESRI.ArcGIS.Client.Geometry.PointCollection polygonPointCollection = new ESRI.ArcGIS.Client.Geometry.PointCollection();

      Int32 index = 0;
      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;

      double srScale;
      if (IsWebMercator() && _xmlConfiguation.WebMercatorScale != 1.0)
        srScale = _xmlConfiguation.WebMercatorScale * _xmlConfiguation.OutputSpatialReferenceUnitsPerMeter;
      else
        srScale = _xmlConfiguation.OutputSpatialReferenceUnitsPerMeter / _xmlConfiguation.MapSpatialReferenceUnitsPerMeter;

      double scaleValue = parcelData.ScaleValue * srScale; 
      double rotationValue = parcelData.RotationValue;

      // Calculate Misclose information
      // 
      Int32 compassTestPoint = -1;
      double compassTotalLength = 0;
      ESRI.ArcGIS.Client.Geometry.MapPoint compassStartPoint = null;
      ESRI.ArcGIS.Client.Geometry.MapPoint compassPoint = _originPoint;
      Int32 lineCount = 0;
      PointDictionary calculatedPointsLocal = new PointDictionary();

      foreach (ParcelLineRow line in parcelRecordData)
      {
        if ((line.GetFrom() == 0) || (line.GetTo() == 0))
          continue;
        if ((line.Category != LineCategory.Boundary) && (line.Category != LineCategory.Road))
          continue;
        if ((compassTestPoint != -1) && (line.GetFrom() != compassTestPoint))
        {
          System.Diagnostics.Debug.Assert(false); // unable to calculate Bowditch rule info
          compassTotalLength = 0;
          break;
        }
        if (compassTestPoint == -1)
          compassStartPoint = compassPoint;
        compassTestPoint = line.GetTo();

        compassTotalLength += line.GetChordDistance();

        ESRI.ArcGIS.Client.Geometry.Polyline compassPolyline;
        ESRI.ArcGIS.Client.Geometry.MapPoint compassNextPoint;
        compassPolyline = GeometryUtil.Line(compassPoint, line.GetBearing(true), line.GetChordDistance(), out compassNextPoint);
        compassPoint = compassNextPoint;
        lineCount++;
      }
      double miscloseDistance = 0;
      double miscloseAngle = 0;
      double miscloseBearing = 0;
      double miscloseRatio = 0;
      if ((lineCount > 1) && (compassPoint != null) && (compassStartPoint != null))
      {
        miscloseAngle = Math.Atan2(compassStartPoint.Y - compassPoint.Y, compassStartPoint.X - compassPoint.X);
        miscloseBearing = Math.PI / 2 - miscloseAngle;
        miscloseDistance = GeometryUtil.LineLength(compassPoint, compassStartPoint);
        if (compassTotalLength > 0)
          miscloseRatio = 1 / (miscloseDistance / compassTotalLength);
      }

      // misclose will be 0 if closure failed
      bool adjustPoints = parcelData.CompassRuleApplied = (miscloseDistance > 0) && 
        ((miscloseDistance <= _xmlConfiguation.MiscloseDistanceSnap) || (miscloseRatio >= _xmlConfiguation.MiscloseRatioSnap)); 

      // Compute lines
      // 
      double tangentBearingRadian = 0; // Assumes tangent to the north as an initial course
      Int32 gridIndex = -1;
      foreach (ParcelLineRow line in parcelRecordData)
      {
        gridIndex++;
        if ((line.GetFrom() == 0) || (line.GetTo() == 0))
          continue;

        ESRI.ArcGIS.Client.Geometry.MapPoint startPointScaled;
        ESRI.ArcGIS.Client.Geometry.MapPoint startPointLocal;
        if (index++ == 0)
        {
          _calculatedPoints.Add(line.GetFrom(), _originPoint);
          calculatedPointsLocal.Add(line.GetFrom(), _originPoint);
          startPointScaled = _originPoint;
          stopBoundaryPoint = _originPoint;

          startPointLocal = new ESRI.ArcGIS.Client.Geometry.MapPoint(0.0, 0.0);
        }
        else
        {
          if (!_calculatedPoints.ContainsKey(line.GetFrom()))
          {
            System.Console.WriteLine("** Missing point coordinate");
            continue;
          }
          startPointScaled = _calculatedPoints[line.GetFrom()];
          startPointLocal = calculatedPointsLocal[line.GetFrom()];
        }

        line.BearingFormat = parcelData.BearingFormat; // make the tangent bearing format line entry in first line

        // If curve is a tangent, calculate it.
        if (line.TangentCurve)
        {
          if (line.Radius == null)
            line.SetBearing(GeometryUtil.RadianToDegree(tangentBearingRadian));
          else
          {
            double lineRadius = line.GetRadius();
            double radialBearing1, radialBearing2;
            GeometryUtil.ConstructCenterPoint(startPointScaled, tangentBearingRadian, line.GetChordDistance(), lineRadius,
                                              line.MinorCurve, SweepDirection.Clockwise, out radialBearing1, out radialBearing2);

            double tangent = line.MinorCurve ? radialBearing1 - Math.PI / 2 : radialBearing1 + Math.PI / 2;
            double angle = tangentBearingRadian - tangent;
            double bearing = tangentBearingRadian + angle;

            bool leftCurve = lineRadius < 0;

            // For left and minor curve, or right and major curves reverse bearing direction
            if ((leftCurve && line.MinorCurve) || (!leftCurve && !line.MinorCurve))
            {
              bearing += Math.PI;
              if (bearing > 2 * Math.PI)
                bearing -= 2 * Math.PI;
            }
            line.SetBearing(GeometryUtil.RadianToDegree(bearing));
          }
        }
        else
          line.SetBearing(line.GetBearing(false));   // update bearing in parcel bearing format

        ESRI.ArcGIS.Client.Geometry.Polyline polylineLocal;
        ESRI.ArcGIS.Client.Geometry.MapPoint nextPointLocal;

        ESRI.ArcGIS.Client.Geometry.Polyline polylineScaled;
        ESRI.ArcGIS.Client.Geometry.MapPoint nextPointScaled;

        // if we where moving pre-calculated points, then we would use an equation like:
        // compassLength += line.GetChordDistance();
        // compassDistanceRatio = compassLength / compassTotalLength;
        double compassDistanceRatio = line.GetChordDistance() / compassTotalLength;

        double scaledRadius = line.GetRadius() * scaleValue;
        double scaledParameter = line.GetParameter2() * scaleValue;
        double scaledDistance = line.GetChordDistance() * scaleValue;
        double correctedBearing = line.GetBearing(true) - rotationValue;
        SweepDirection sweep = SweepDirection.Clockwise; 
        if (correctedBearing < 0)
          correctedBearing += Math.PI * 2;

        if (_calculatedPoints.ContainsKey(line.GetTo()) && calculatedPointsLocal.ContainsKey(line.GetTo()))
        {
          nextPointScaled = _calculatedPoints[line.GetTo()];
          nextPointLocal = calculatedPointsLocal[line.GetTo()];

          if (line.Radius == null)
          {
            polylineScaled = GeometryUtil.Line(startPointScaled, nextPointScaled);
            polylineLocal = GeometryUtil.Line(startPointLocal, nextPointLocal);
          }
          else
          {
            polylineScaled = GeometryUtil.ConstructArcSegment(startPointScaled, nextPointScaled, scaledRadius, line.MinorCurve, sweep);
            polylineLocal = GeometryUtil.ConstructArcSegment(startPointLocal, nextPointLocal, line.GetRadius(), line.MinorCurve, sweep);
          }
        }
        else if (adjustPoints)  
        {
          // Adjust end point and create line using two end points.
          ESRI.ArcGIS.Client.Geometry.MapPoint scaledAdjustedNextPoint;
          GeometryUtil.Line(startPointScaled, correctedBearing, scaledDistance, out nextPointScaled);
          GeometryUtil.Line(nextPointScaled, miscloseBearing - rotationValue, miscloseDistance * compassDistanceRatio * scaleValue, out scaledAdjustedNextPoint);
          nextPointScaled = scaledAdjustedNextPoint;

          ESRI.ArcGIS.Client.Geometry.MapPoint localAdjustedNextPoint;
          GeometryUtil.Line(startPointLocal, line.GetBearing(true), line.GetChordDistance(), out nextPointLocal);
          GeometryUtil.Line(nextPointLocal, miscloseBearing - rotationValue, miscloseDistance * compassDistanceRatio * scaleValue, out localAdjustedNextPoint);
          nextPointLocal = localAdjustedNextPoint;

          if (line.Radius == null)
          {
            polylineScaled = GeometryUtil.Line(startPointScaled, nextPointScaled);
            polylineLocal = GeometryUtil.Line(startPointLocal, nextPointLocal);
          }
          else
          {
            polylineScaled = GeometryUtil.ConstructArcSegment(startPointScaled, nextPointScaled, scaledRadius, line.MinorCurve, sweep);
            polylineLocal = GeometryUtil.ConstructArcSegment(startPointLocal, nextPointLocal, line.GetRadius(), line.MinorCurve, sweep);
          }

          if ((nextPointScaled != null) && (nextPointLocal != null))
          {
            _calculatedPoints.Add(line.GetTo(), nextPointScaled);
            calculatedPointsLocal.Add(line.GetTo(), nextPointLocal);
          }
        }
        else 
        {
          if (line.Radius == null)
          {
            polylineScaled = GeometryUtil.Line(startPointScaled, correctedBearing, scaledDistance, out nextPointScaled);
            polylineLocal = GeometryUtil.Line(startPointLocal, line.GetBearing(true), line.GetChordDistance(), out nextPointLocal);
          }
          else
          {
            polylineScaled = GeometryUtil.ConstructArcSegment(startPointScaled, correctedBearing, scaledParameter, scaledRadius, line.MinorCurve, sweep, out nextPointScaled);
            polylineLocal = GeometryUtil.ConstructArcSegment(startPointLocal, line.GetBearing(true), line.GetChordDistance(), line.GetRadius(), line.MinorCurve, sweep, out nextPointLocal);
          }

          if ((nextPointScaled != null) && (nextPointLocal != null))
          {
            _calculatedPoints.Add(line.GetTo(), nextPointScaled);
            calculatedPointsLocal.Add(line.GetTo(), nextPointLocal);
          }
        }

        double lineBearing = line.GetBearing(true);
        if (line.Radius == null)
          tangentBearingRadian = lineBearing;
        else
        {
          // calculate radial bearing based on non-scaled/rotated lines.
          double radialBearing1, radialBearing2;
          GeometryUtil.ConstructCenterPoint(startPointScaled, lineBearing, line.GetChordDistance(), line.GetRadius(), line.MinorCurve, sweep, out radialBearing1, out radialBearing2);
          if (double.IsNaN(radialBearing2))
            tangentBearingRadian = lineBearing;
          else
          {
            tangentBearingRadian = radialBearing2 - Math.PI / 2;
            if (line.GetRadius() < 0)  
            {
              // If left curve, we to reverse bearing direction
              tangentBearingRadian += Math.PI;
              if (tangentBearingRadian > Math.PI * 2)
                tangentBearingRadian -= Math.PI * 2;
            }
          }
        }

        ESRI.ArcGIS.Client.Geometry.MapPoint centerPoint = null;
        if (line.Radius != null)
        {
          double bearing1, bearing2;
          centerPoint = GeometryUtil.ConstructCenterPoint(startPointScaled, correctedBearing, scaledParameter, scaledRadius, line.MinorCurve, sweep, out bearing1, out bearing2);
          if (centerPoint != null)
          {
            Int32 centerId2 = line.CenterPoint.GetValueOrDefault(NextPointId(line.GetFrom()));
            
            line.SetCurveAttributes(centerId2, bearing1, bearing2);

            if (!_calculatedPoints.ContainsKey(centerId2))
              _calculatedPoints.Add(centerId2, centerPoint);

            if (!_centerPoints.ContainsKey(centerId2))
              _centerPoints.Add(centerId2, true);
          }
          else
            line.ResetCurveAttributes();
        }
        else
          line.ResetCurveAttributes();

        // Collect boundary lines point segments for parcel area calculation.
        if ((line.Category == LineCategory.Boundary) || (line.Category == LineCategory.Road))
          if ((polylineLocal != null) && (polylineLocal.Paths != null))
            foreach (ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection in polylineLocal.Paths)
              foreach (ESRI.ArcGIS.Client.Geometry.MapPoint plPoint in pointCollection)
                polygonPointCollection.Add(plPoint);

        // Misclose info
        if ((startBoundaryPointId == -1) && (line.Category == LineCategory.Boundary))
        {
          startBoundaryPoint = startPointScaled;
          startBoundaryPointId = line.GetFrom();
        }
        if ((startBoundaryPointId != -1) && (line.Category != LineCategory.Boundary) && (stopBoundaryPointId == -1))
          stopBoundaryPointId = lastBoundaryPointId;
        if ((startBoundaryPointId != -1) && (stopBoundaryPointId == -1))
        {
          ESRI.ArcGIS.Client.Geometry.MapPoint lastMeasurePoint = stopBoundaryPoint;
          stopBoundaryPoint = GeometryUtil.ConstructPoint(lastMeasurePoint, line.GetBearing(true), line.GetChordDistance());
          lastBoundaryPointId = line.GetTo();
        }

        if (polylineScaled != null)
        {
          AddGraphicLine(line, correctedBearing, ref lineGraphicsLayer, ref polylineScaled, gridIndex);

          if (_srSnapPointId == line.GetTo())
            AddGraphicPoint(ref pointGraphicsLayer, ref nextPointScaled, true);
          else
            AddGraphicPoint(ref pointGraphicsLayer, ref nextPointScaled, false);

          // Draw center point
          if (centerPoint != null)
            AddGraphicCenterPoint(ref pointGraphicsLayer, ref centerPoint);
        }
        else
        {
          System.Console.WriteLine("** Error creating graphic object");
        }
      }

      if ((startBoundaryPointId != -1) && (stopBoundaryPointId == -1))
        stopBoundaryPointId = lastBoundaryPointId;

      if ((startBoundaryPoint != null) && (stopBoundaryPoint != null) &&
          (startBoundaryPointId != -1) && (stopBoundaryPointId != -1))
      {
        double angleRad = Math.PI / 2 - Math.Atan2(startBoundaryPoint.Y - stopBoundaryPoint.Y, startBoundaryPoint.X - stopBoundaryPoint.X);
        double angle = GeometryUtil.RadianToDegree(angleRad);
        if (angle < 0)
          angle += 360;

        ESRI.ArcGIS.Client.Geometry.Polygon polygon = new ESRI.ArcGIS.Client.Geometry.Polygon();
        polygonPointCollection.Add(new ESRI.ArcGIS.Client.Geometry.MapPoint(0.0, 0.0)); // Close parcel.
        polygon.Rings.Add(polygonPointCollection);
        double area = ESRI.ArcGIS.Client.Geometry.Euclidian.Area(polygon);
         
        double miscloseDistanceConv = miscloseDistance * (_xmlConfiguation.SpatialReferenceUnitsPerMeter / _xmlConfiguation.EntryUnitsPerMeter);

        double miscloseBearingDeg = GeometryUtil.RadianToDegree(miscloseBearing);
        if (miscloseBearingDeg < 0)
          miscloseBearingDeg += 360;
        parcelData.SetMiscloseInfo(miscloseBearingDeg, miscloseDistanceConv, area, miscloseRatio);  // automatically mark misclose error to false
      }
      else
        parcelData.MiscloseError = true;  // this will automatically zero out misclose distance/bearing/area
    }

    private bool BearingDistanceToPoint(Int32 pointId, out double bearing, out double distance, out ESRI.ArcGIS.Client.Geometry.MapPoint snapPoint)
    {
      bearing = 0.0;
      distance = 0.0;
      snapPoint = null;
      if ((_originPoint == null) || (pointId == -1))
        return false;

      PointDictionary rawPoints = new PointDictionary();

      Int32 index = 0;
      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;
      foreach (ParcelLineRow line in parcelRecordData)
      {
        if ((line.GetFrom() == 0) || (line.GetTo() == 0))
          continue;

        ESRI.ArcGIS.Client.Geometry.MapPoint startPoint;
        if (index++ == 0)
        {
          rawPoints.Add(line.GetFrom(), _originPoint);
          startPoint = _originPoint;
        }
        else
        {
          if (!rawPoints.ContainsKey(line.GetFrom()))
          {
            System.Console.WriteLine("** Missing point coordinate");
            continue;
          }
          startPoint = rawPoints[line.GetFrom()];
        }

        if (!rawPoints.ContainsKey(line.GetTo()))
        {
          double correctedBearing = line.GetBearing(true);
          if (correctedBearing < 0)
            correctedBearing += Math.PI * 2;

          ESRI.ArcGIS.Client.Geometry.MapPoint nextPoint;
          GeometryUtil.Line(startPoint, correctedBearing, line.GetChordDistance(), out nextPoint);

          if (nextPoint != null)
            rawPoints.Add(line.GetTo(), nextPoint);
        }

        if (rawPoints.ContainsKey(pointId))
        {
          snapPoint = rawPoints[pointId];

          distance = GeometryUtil.LineLength(_originPoint, snapPoint);

          ESRI.ArcGIS.Client.Geometry.MapPoint azPoint =
            new ESRI.ArcGIS.Client.Geometry.MapPoint(_originPoint.X + distance, _originPoint.Y);

          bearing = Math.PI / 2 - GeometryUtil.Angle(azPoint, snapPoint, _originPoint);

          return true;
        }
      }
      return false;
    }
    #endregion Draw and calculate sketch

    #region Parcel Entry

    public class CreateXMLInfo
    {
      public CreateXMLInfo(string targetFile, bool email)
      {
        _targetFile = targetFile;
        _emailResult = email;
      }

      private string _targetFile;
      public string TargetFile
      {
        get { return _targetFile; }
      }

      private bool _emailResult;
      public bool EmailResult
      {
        get { return _emailResult; }
      }
    }

    public void CreateXML(string targetFile, bool emailResult)
    {
      // This routine sets up the initial point location of the parcel.
      // It gets the XY location of the point, and buffers the point and 
      // issues a query to find a point to snap tool.

      GeometryService geometryServiceProject = new GeometryService(_xmlConfiguation.GeometryServerUrl);
      if (geometryServiceProject == null)
      {
        MessageBox.Show((string)Application.Current.FindResource("strFailedToCreateXMLGeometryService"), (string)Application.Current.FindResource("strTitle"));
        return;
      }

      geometryServiceProject.ProjectCompleted += GeometryService_CreateXMLFile;
      geometryServiceProject.Failed += GeometryService_Failed;
      geometryServiceProject.CancelAsync();

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      ObservableCollection<ParcelLineRow> parcelRecord = parcelData.GetRecordInfo();
      if (parcelRecord.Count == 0)
      {
        MessageBox.Show((string)Application.Current.FindResource("strFailedToCreateXML"), (string)Application.Current.FindResource("strTitle"));
        return;
      }

      Int32 pointId = parcelRecord[0].GetFrom();
      ESRI.ArcGIS.Client.Geometry.MapPoint sourcePoint = null;
      if (_calculatedPoints.ContainsKey(pointId))
        sourcePoint = _calculatedPoints[pointId];
      if (sourcePoint == null)
      {
        MessageBox.Show((string)Application.Current.FindResource("strFailedToCreateXML"), (string)Application.Current.FindResource("strTitle"));
        return;
      }

      if (_xmlConfiguation.OutputSpatialReference == null)
      {
        CreateXMLFile(targetFile, emailResult, sourcePoint);
        return;
      }

      Graphic projectingGraphic = new Graphic();
      projectingGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      projectingGraphic.Geometry = sourcePoint;

      // Input spatial reference for buffer operation defined by first feature of input geometry array
      projectingGraphic.Geometry.SpatialReference = ParcelMap.SpatialReference;

      var graphicList = new List<Graphic>();
      graphicList.Add(projectingGraphic);

      if (_xmlConfiguation.HasDatumTransformation)
      {
        DatumTransform transformation = new DatumTransform();
        if (_xmlConfiguation.DatumTransformationWKID > 0)
          transformation.WKID = _xmlConfiguation.DatumTransformationWKID;
        else
          transformation.WKT = _xmlConfiguation.DatumTransformationWKT;

        //geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference,
        //  transformation, _xmlConfiguation.DatumTransformationForward, new CreateXMLInfo(targetFile, emailResult));

        geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference,
transformation);
      }
      else
        geometryServiceProject.ProjectAsync(graphicList, _xmlConfiguation.OutputSpatialReference, new CreateXMLInfo(targetFile, emailResult));
    }

    void GeometryService_CreateXMLFile(object sender, GraphicsEventArgs args)
    {
      CreateXMLInfo info = (CreateXMLInfo)args.UserState;
      MapPoint projectedStartPoint = (MapPoint)args.Results[0].Geometry;

      CreateXMLFile(info.TargetFile, info.EmailResult, projectedStartPoint);
    }

    void CreateXMLFile(string targetFile, bool emailResult, MapPoint startPoint)
    {
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      DocumentEntry documentType = parcelData.DocumentEntries.CurrentItem as DocumentEntry;

      double scale = parcelData.ScaleValue;
      double rotation = -GeometryUtil.RadianToDegree(parcelData.RotationValue);

      VerifyAndFixUnit();

      MapPoint projectedStartPoint = startPoint;
      if (!ParcelXML.WriteCEXML(ref parcelData, targetFile, ref _xmlConfiguation, ParcelMap.SpatialReference, ref _calculatedPoints, ref projectedStartPoint, scale, rotation))
      {
        MessageBox.Show((string)Application.Current.FindResource("strFailedToCreateXML"), (string)Application.Current.FindResource("strTitle"));
        return;
      }

      if (!emailResult)  // Return here if writing to a file
        return;

      Microsoft.Office.Interop.Outlook.Application oApp = new Microsoft.Office.Interop.Outlook.Application();
      if (oApp == null)
        MessageBox.Show((string)Application.Current.FindResource("strOutlookStartError"),
                        (string)Application.Current.FindResource("strTitle"));
      else
      {
        try
        {
          Microsoft.Office.Interop.Outlook._MailItem outlookMailItem = (Microsoft.Office.Interop.Outlook._MailItem)oApp.CreateItem(Microsoft.Office.Interop.Outlook.OlItemType.olMailItem);
          if (outlookMailItem == null)
            MessageBox.Show((string)Application.Current.FindResource("strOutlookStartError"),
                            (string)Application.Current.FindResource("strTitle"));
          else
          {
            outlookMailItem.Attachments.Add(targetFile);
            outlookMailItem.Body = (string)Application.Current.FindResource("strParcelShareBody");
            outlookMailItem.Subject = (string)Application.Current.FindResource("strNew") + " " + documentType.SimpleName() + " " + parcelData.PlanName;
            outlookMailItem.To = _xmlConfiguation.MailTo;
            outlookMailItem.Display(true);
          }
        }
        catch (Exception e1)
        {
          System.Diagnostics.Debug.WriteLine("Exception: {0}", e1);
          MessageBox.Show(e1.Message, (string)Application.Current.FindResource("strTitle"));
        }
      }

    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      DocumentEntry documentType = parcelData.DocumentEntries.CurrentItem as DocumentEntry;

      Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
      dlg.FileName = documentType.SimpleName() + " " + parcelData.PlanName; // Default file name 
      dlg.DefaultExt = ".xml"; // Default file extension 
      dlg.Filter = "Cadastral XML|*.xml"; // Filter files by extension 

      // Show save file dialog box 
      Nullable<bool> result = dlg.ShowDialog();

      // Process save file dialog box results 
      if (result == false)
        return;

      CreateXML(dlg.FileName, false);
    }

    private void Send_AsEmail(object sender, RoutedEventArgs e)
    {
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      DocumentEntry documentType = parcelData.DocumentEntries.CurrentItem as DocumentEntry;

      string fileName = Path.GetTempPath() + documentType.SimpleName() + " " + parcelData.PlanName;
      fileName = fileName.Trim() + ".xml";

      CreateXML(fileName, true);
    }

    private void VerifyAndFixUnit()
    {
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      _xmlConfiguation.EntryFormat = parcelData.BearingFormat;

      string lastUnit = "";
      foreach (Layer layer in ParcelMap.Layers)
      {
        ArcGISDynamicMapServiceLayer dynamicMS = layer as ArcGISDynamicMapServiceLayer;
        if ((dynamicMS != null) && (dynamicMS.Units != null) && (dynamicMS.Units != ""))
          lastUnit = dynamicMS.Units;

        if (lastUnit == "")
        {
          ArcGISTiledMapServiceLayer tilesMS = layer as ArcGISTiledMapServiceLayer;
          if ((tilesMS != null) && (tilesMS.Units != null) && (tilesMS.Units != ""))
            lastUnit = tilesMS.Units;
        }
      }

      _xmlConfiguation.MapSpatialReferenceUnits = lastUnit;

      parcelData.Configuration = _xmlConfiguation;  // update units
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
      if (MessageBox.Show((string)Application.Current.FindResource("strDiscardRecordInfo"), 
                          (string)Application.Current.FindResource("strTitle"), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
      {
        ResetGrid();
        CalculateAndAddLineGraphics();
      }
    }

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
      long count = _calculatedPoints.Count;
      if (count < 2)
        return;

      double x1, y1, x2, y2;
      x1 = x2 = _originPoint.X;
      y1 = y2 = _originPoint.Y;
      foreach (KeyValuePair<Int32, ESRI.ArcGIS.Client.Geometry.MapPoint> mapPoint in _calculatedPoints)
      {
        if (_centerPoints.ContainsKey(mapPoint.Key))  // skip center points.
          continue;

        double x = mapPoint.Value.X;
        double y = mapPoint.Value.Y;
        if (x < x1) x1 = x;
        if (x > x2) x2 = x;
        if (y < y1) y1 = y;
        if (y > y2) y2 = y;
      }

      ESRI.ArcGIS.Client.Geometry.Envelope envelope = new ESRI.ArcGIS.Client.Geometry.Envelope(x1, y1, x2, y2);
      ESRI.ArcGIS.Client.Geometry.Envelope envelopeExpanded = envelope.Expand(1.35);
      ParcelMap.ZoomTo(envelopeExpanded);
    }

    // WPF DataGrid moves input focus and selection to the wrong/first cell when pressing tab
    //   http://connect.microsoft.com/VisualStudio/feedback/details/697634/wpf-datagrid-moves-input-focus-and-selection-to-the-wrong-cell-when-pressing-tab
    //
    // To work around the above problem, we call CommitRows on the last row. 
    // This causes the grid to visually add an empty row when an edit is made to a new/empty row.
    //
    // We are not using the solution from the above link, because it does not
    // work well with 'Treat Enter as Tabs in grid' below.
    //
    private void ParcelLines_PreviewKeyUp(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        // We only need to call CommitEdit once pre-last row.
        Int32 row = ParcelLines.Items.CurrentPosition;
        if ((row != _lastRowCommited) && (ParcelLines.Items.Count - 2 == row))
        {
          ParcelLines.CommitEdit();
          _lastRowCommited = row;
        }

        // To work around having a incorrect selection after pressing enter, just unselect all.
        ParcelLines.UnselectAllCells();
      }

      if (e.Key == Key.Delete)            // Don't do this in preview key
        CalculateAndAddLineGraphics();    // since the delete has not happened yet.
    }

    // Treat Enter as Tabs in grid.
    //
    // We can code it as an Attached Property with: (ps: it does work)
    //   http://madprops.org/blog/enter-to-tab-as-an-attached-property/
    //
    // In this application, we will keep it simple and use the KeyDown approach.
    //
    private void ParcelLines_PreviewKeyDown(object sender, KeyEventArgs e)
    {
      Int32 row = ParcelLines.Items.CurrentPosition;

      UIElement uiElement = e.OriginalSource as UIElement;

      if (e.Key == Key.Enter)
      {
        if (AutoCompleteCells(sender, e))
        {
          _moveToBearingColumn = true;
          // We are relying on the grid behavior to move a cell down.
          // On Key up we move to the bearing column.
        }
        else if (e.Handled == false)  // If enter is pressed, act like a tab
        {
          e.Handled = true;
          uiElement.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
      }

      // See following link for view model impl.
      //   http://stackoverflow.com/questions/4411578/delete-cell-content-in-a-wpf-datagrid-when-the-delete-key-is-pressed
      //
      if (e.Key == Key.Delete)  // Delete cell contents
      {
        DataGridCell cell = e.OriginalSource as DataGridCell;
        if (cell == null)
          return;

        if (!cell.IsReadOnly && cell.IsEnabled)
        {
          TextBlock tb = cell.Content as TextBlock;
          if (tb != null)
          {
            Binding binding = BindingOperations.GetBinding(tb, TextBlock.TextProperty);
            if (binding == null)
              return;

            BindingExpression exp = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
            PropertyInfo propertyInfo = exp.DataItem.GetType().GetProperty(binding.Path.Path);
            if (propertyInfo != null)
            {
              try
              {
                propertyInfo.SetValue(exp.DataItem, null, null);
                // exp.UpdateTarget();  // single field (not required now that we are using INotifyPropertyChanged
              }
              catch (Exception)
              {
                System.Diagnostics.Debug.Assert(false);  // why are we getting this area? Anything null?
              }
            }
          }
        }
      }
    }

    private bool AutoCompleteCells(object sender, KeyEventArgs e)
    {
      DataGridColumn dgColumn = ParcelLines.CurrentColumn;
      bool isDistance = dgColumn.DisplayIndex == (int)ColumnIndex.Distance;
      bool isParameter2 = dgColumn.DisplayIndex == (int)ColumnIndex.Parameter2;
      bool isRadius = dgColumn.DisplayIndex == (int)ColumnIndex.Radius;
      if (!isDistance && !isParameter2 && !isRadius)
        return false;

      ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;

      // Step through the visual tree to get the DataGridRow
      //   http://social.msdn.microsoft.com/Forums/en/wpf/thread/b5c09e2c-9bc7-4c89-b354-a38b5917b899

      DependencyObject dep = (DependencyObject)e.OriginalSource;

      // Walk up dependency tree and find cell
      while ((dep != null) && !(dep is System.Windows.Controls.DataGridCell))
        dep = VisualTreeHelper.GetParent(dep);
      if (dep == null)
        return false;
      DataGridCell gdCell = dep as DataGridCell;

      // If distance/parameter2 is null, return false to tab to next field.
      // We can't access the data in parcelData[rowIndex].Distance, since it's not committed yet.
      TextBox gridTextBox = gdCell.Content as TextBox;
      if (gridTextBox == null)
        return false;
      string gridTextString = gridTextBox.Text.Trim();
      if (gridTextString.Length == 0)
        return false;

      // Walk up dependency tree and find row
      while ((dep != null) && !(dep is System.Windows.Controls.DataGridRow))
        dep = VisualTreeHelper.GetParent(dep);
      if (dep == null)
        return false;

      DataGridRow dgRow = dep as DataGridRow;
      int rowIndex = ParcelLines.ItemContainerGenerator.IndexFromContainer(dgRow);

      // The current entered cell is not yet on the row instance. Force it here.
      ParcelLineRow currentLine = parcelRecordData[rowIndex];
      if (isDistance)
        currentLine.Distance = gridTextString;
      if (isRadius)
        currentLine.Radius = gridTextString;
      if (isParameter2)
        currentLine.Parameter2 = gridTextString;

      if (currentLine.GetChordDistance() == 0)
        return false;

      // Auto fill in "to" on current row and "from" on next row.
      if (currentLine.GetTo() == 0)
      {
        Int32 nextId = NextPointId(currentLine.GetFrom());
        currentLine.To = nextId.ToString();

        if (parcelRecordData.Count() - 1 == rowIndex)
        {
          ParcelLineRow lineRow = new ParcelLineRow(ref _xmlConfiguation);
          lineRow.From = nextId.ToString();
          lineRow.Bearing = "*"; // tangent

          parcelRecordData.Add(lineRow);
        }
        else if (parcelRecordData[rowIndex + 1].GetFrom() == 0)
        {
          parcelRecordData[rowIndex + 1].From = nextId.ToString();
        }
      }

      return true;
    }

    private int NextPointId(Int32 seedvalue)
    {
      Int32 nextId = seedvalue + 1;
      while (_calculatedPoints.ContainsKey(nextId))
        nextId++;
      return nextId;
    }

    private void ParcelLines_KeyUp(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)        // if the key is handled, we will not get this.
      {
        ParcelLines.CommitEdit();    // Commit changes to data structure
        CalculateAndAddLineGraphics();           // and draw graphics for them.
      }
      // if the user has pressed enter on <distance> or <parameter2> then 
      // we allowed the grid to move a cell down (not to the right).
      // in this case, move to the distance field for next entry.
      if (_moveToBearingColumn)
      {
        _moveToBearingColumn = false;
        ParcelLines.CurrentColumn = ParcelLines.Columns[(int)ColumnIndex.Bearing];
      }
    }

    #endregion Parcel Entry

    #region Drag and Drop

    private void DataGrid_MouseMove(object sender, MouseEventArgs e)
    {
      // This is what we're using as a cue to start a drag.
      if (e.LeftButton == MouseButtonState.Pressed)
      {
        ObservableCollection<ParcelLineRow> parcelRecordData = ParcelLines.ItemsSource as ObservableCollection<ParcelLineRow>;

        // Find the row and only drag it if it is already selected.
        DataGridRow row = FindVisualParent<DataGridRow>(e.OriginalSource as FrameworkElement);
        if ((row != null) && row.IsSelected)
        {
          // Perform the drag operation
          ParcelLineRow selectedRow = row.Item as ParcelLineRow;  // DragDropEffects.. we did pass in selectedRow.

          DragDropEffects finalDropEffect = DragDrop.DoDragDrop(row, row.Item, DragDropEffects.Move);
          if ((finalDropEffect == DragDropEffects.Move) && (_targetRow != null))
          {
            // A Move drop was accepted

            // Determine the index of the item being dragged and the drop
            // location. If they are difference, then move the selected
            // item to the new location.

            int oldIndex = parcelRecordData.IndexOf(selectedRow);
            int newIndex = parcelRecordData.IndexOf(_targetRow);
            if (oldIndex != newIndex)
            {
              if ((oldIndex != newIndex) && (oldIndex >= 0))
              {
                parcelRecordData.Move(oldIndex, newIndex);
              }
              else if (newIndex >= 0)
              {
                // When dragging in last empty row, create a new one.

                ParcelLineRow newRow = new ParcelLineRow(ref _xmlConfiguation);
                parcelRecordData.Insert(newIndex, newRow);
              }
              CalculateAndAddLineGraphics();
            }
            _targetRow = null;
          }
        }
      }
    }

    private void ParcelLines_AddingNewItem(object sender, System.Windows.Controls.AddingNewItemEventArgs e)
    {
      ParcelLineRow newRow = new ParcelLineRow(ref _xmlConfiguation);
      e.NewItem = newRow;
    }

    private void DataGrid_CheckDropTarget(object sender, DragEventArgs e)
    {
      DataGridRow row = FindVisualParent<DataGridRow>(e.OriginalSource as UIElement);
      if ((row == null) || !(row.Item is ParcelLineRow))
      {
        // Not over a DataGridRow that contains a ParcelLineRow object
        e.Effects = DragDropEffects.None;
      }

      e.Handled = true;
    }

    private void DataGrid_Drop(object sender, DragEventArgs e)
    {
      e.Effects = DragDropEffects.None;
      e.Handled = true;

      // Verify that this is a valid drop and then store the drop target
      DataGridRow row = FindVisualParent<DataGridRow>(e.OriginalSource as UIElement);
      if (row != null)
      {
        _targetRow = row.Item as ParcelLineRow;
        if (_targetRow != null)
        {
          e.Effects = DragDropEffects.Move;
        }
      }
    }

    #endregion

    #region Helper

    private static T FindVisualParent<T>(UIElement element) where T : UIElement
    {
      UIElement parent = element;
      while (parent != null)
      {
        T correctlyTyped = parent as T;
        if (correctlyTyped != null)
        {
          return correctlyTyped;
        }

        parent = VisualTreeHelper.GetParent(parent) as UIElement;
      }

      return null;
    }

    #endregion

    ParcelLineRow _targetRow;
  }
}
