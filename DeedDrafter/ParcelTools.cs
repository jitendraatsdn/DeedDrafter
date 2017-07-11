using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Tasks;
using Utilities;

namespace DeedDrafter
{
  /// <summary>
  // Code behind for the parcel tools.
  // 
  // Current tools:
  // a) Rotate parcel lines (w/ snapping)
  // b) Scale parcel lines (w/ snapping)
  /// </summary>
  /// 
  public partial class MainWindow : Window
  {
    ESRI.ArcGIS.Client.Geometry.MapPoint _srPoint = null;
    ESRI.ArcGIS.Client.Geometry.MapPoint _srSnapPoint = null;
    Int32 _srSnapPointId = -1;
    double _srDistanceToPoint;
    double _srBearingToPoint;

    ESRI.ArcGIS.Client.Geometry.MapPoint _lastGeometryCP = null;
    double _lastSearchDistance = -1.0;
    bool _lastBufferBasedOnCurve = true;

    // Tread safe
    ConcurrentDictionary<Int32, ESRI.ArcGIS.Client.Geometry.Geometry> _snapObjects = new ConcurrentDictionary<Int32, ESRI.ArcGIS.Client.Geometry.Geometry>();

    private void ScaleRotate(Point srcPoint)
    {
      if (!PDE_Tools.IsExpanded)
        return;

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;

      _moveScale = _oldScale = parcelData.ScaleValue;
      _moveRotation = _oldRotation = parcelData.RotationValue;
      _srPoint = ParcelMap.ScreenToMap(srcPoint);
      _srSnapPoint = null;

      double spX = _srPoint.X;
      double spY = _srPoint.Y;

      // Find _startPoint in list of points. If "close" snap to that point.
      // Otherwise user can free form scale or rotate parcel lines.

      double shortestDistance = double.MaxValue;
      Int32 shortestId = -1;
      ESRI.ArcGIS.Client.Geometry.MapPoint foundPoint = null;
      foreach (KeyValuePair<Int32, ESRI.ArcGIS.Client.Geometry.MapPoint> kvp in _calculatedPoints)
      {
        double x = kvp.Value.X;
        double y = kvp.Value.Y;
        double distance = GeometryUtil.LineLength(spX, spY, x, y);
        if ((distance < shortestDistance) && (distance < _xmlConfiguation.SnapTolerance))
        {
          shortestDistance = distance;
          shortestId = kvp.Key;
          foundPoint = new ESRI.ArcGIS.Client.Geometry.MapPoint(x, y);
        }
      }

      if (BearingDistanceToPoint(shortestId, out _srBearingToPoint, out _srDistanceToPoint, out _srSnapPoint))
      {
        // BearingDistanceToPoint will fail if shortestId == -1

        _srSnapPointId = shortestId;
        if (RotationButton.IsChecked == true)
        {
          double radialSearch = _srDistanceToPoint * parcelData.ScaleValue; 
          
          // We seem to be getting some numerical precision error when rotating... this does not
          // really matter here; we only need to re-buffer if the changes are > 0.1.

          // if the user re-rotate with the same rotate point, try to avoid re-caching.
          if ((_originPoint == null) || (_lastGeometryCP == null) ||
              (Math.Abs(_lastSearchDistance - radialSearch) > 0.1) || !_lastBufferBasedOnCurve ||
              (_lastGeometryCP.X != _originPoint.X) || (_lastGeometryCP.Y != _originPoint.Y))
          {
            ESRI.ArcGIS.Client.Geometry.MapPoint offsetOriginPoint = new ESRI.ArcGIS.Client.Geometry.MapPoint(_originPoint.X - radialSearch, _originPoint.Y);

            // Create a geometry circle from the anchor/rotating point to the snap point. 
            // We will create create a cache of all these points within the buffer distance
            // of this circle.

            ESRI.ArcGIS.Client.Geometry.MapPoint endPoint;
            ESRI.ArcGIS.Client.Geometry.Polyline circle = GeometryUtil.ConstructArcSegment(offsetOriginPoint, 0.0, 0.001, radialSearch, false, SweepDirection.Counterclockwise, out endPoint);

            _lastGeometryCP = _originPoint;
            _lastSearchDistance = radialSearch;
            _lastBufferBasedOnCurve = true;

            CacheSnapObjects(circle, radialSearch);
          }
        }
        else if (ScaleButton.IsChecked == true)
        {
          double mapDistanceBuffer = _srDistanceToPoint * 1.5 * parcelData.ScaleValue;

          // if the user re-scales with the same scale point, try to avoid re-caching.
          if ((_originPoint == null) || (_lastGeometryCP == null) ||
              (_lastSearchDistance < mapDistanceBuffer) || _lastBufferBasedOnCurve ||
              (_lastGeometryCP.X != _originPoint.X) || (_lastGeometryCP.Y != _originPoint.Y))
          {
            // Create a line from the anchor/rotating point to the snap point * 1.5 of the distance.
            // We will create create a cache of all these points within the buffer distance
            // of this line.

            ESRI.ArcGIS.Client.Geometry.MapPoint endPoint;
            ESRI.ArcGIS.Client.Geometry.Polyline snapLine = GeometryUtil.Line(_originPoint,
                                                                 _srBearingToPoint - parcelData.RotationValue,
                                                                 mapDistanceBuffer,
                                                                 out endPoint);
            if (snapLine != null)
            {
              _lastGeometryCP = _originPoint;
              _lastSearchDistance = mapDistanceBuffer;
              _lastBufferBasedOnCurve = false;

              CacheSnapObjects(snapLine, mapDistanceBuffer);
            }
          }
        }
        // else no snapping.

        CalculateAndAddLineGraphics();      // Redraw so we have snap graphic shown
      }
      else                      // BearingDistanceToPoint returns false if id = -1
        _srSnapPointId = -1;
    }

    private void CacheSnapObjects(ESRI.ArcGIS.Client.Geometry.Geometry geometryObject, double snapDistance)
    {
      // For the given geometry (line or circle), find all the features that fall within the snapDistance.
      // First we need to issue a buffer in this method. GeometryService_LineBufferCompleted will
      // do the feature query.

      _snapObjects.Clear();
      GeometryService geometryServiceScaleRotate = new GeometryService(_xmlConfiguation.GeometryServerUrl);
      if (geometryServiceScaleRotate == null)
        return;

      geometryServiceScaleRotate.BufferCompleted += GeometryService_LineBufferCompleted;
      geometryServiceScaleRotate.Failed += GeometryService_Failed;
      geometryServiceScaleRotate.CancelAsync();

      Graphic clickGraphic = new Graphic();
      clickGraphic.Symbol = LayoutRoot.Resources["DefaultMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      clickGraphic.Geometry = geometryObject;

      // Input spatial reference for buffer operation defined by first feature of input geometry array
      clickGraphic.Geometry.SpatialReference = ParcelMap.SpatialReference;

      // If buffer spatial reference is GCS and unit is linear, geometry service will do geodesic buffering
      ESRI.ArcGIS.Client.Tasks.BufferParameters bufferParams = new ESRI.ArcGIS.Client.Tasks.BufferParameters()
      {
        BufferSpatialReference = ParcelMap.SpatialReference,
        OutSpatialReference = ParcelMap.SpatialReference,
      };
      bufferParams.Distances.Add(snapDistance);
      bufferParams.Features.Add(clickGraphic);

      System.Diagnostics.Debug.WriteLine("Async: Buffering potential candidates for snapping.");
      geometryServiceScaleRotate.BufferAsync(bufferParams, snapDistance);
    }

    void GeometryService_LineBufferCompleted(object sender, GraphicsEventArgs args)
    {
      if (args.Results.Count == 0)
        return;

      Graphic bufferGraphic = new Graphic();
      bufferGraphic.Geometry = args.Results[0].Geometry;
      bufferGraphic.Symbol = LayoutRoot.Resources["BufferSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol;
      bufferGraphic.SetZIndex(1);

      // Draw buffered graphic on map (for debugging)
      //
      // GraphicsLayer testGraphicsLayer = ParcelMap.Layers["TestGraphicLayer"] as GraphicsLayer;
      // testGraphicsLayer.ClearGraphics();
      // testGraphicsLayer.Graphics.Add(bufferGraphic);

      _snapObjects.Clear();
      foreach (LayerDefinition defn in _xmlConfiguation.SnapLayers)
        GetFeaturesInBuffer(bufferGraphic, defn.Layer(), args.UserState);
    }

    void GetFeaturesInBuffer(Graphic bufferGraphic, string layer, object searchDistance)
    {
      QueryTask queryTask = new QueryTask(layer);
      if (queryTask == null)
        return;
      queryTask.ExecuteCompleted += QueryTask_ExecuteCompleted;
      queryTask.Failed += QueryTask_Failed;
      queryTask.DisableClientCaching = true;
      queryTask.CancelAsync();

      ESRI.ArcGIS.Client.Tasks.Query query = new ESRI.ArcGIS.Client.Tasks.Query();
      query.ReturnGeometry = true;
      query.OutSpatialReference = ParcelMap.SpatialReference;
      query.Geometry = bufferGraphic.Geometry;

      System.Diagnostics.Debug.WriteLine("Async: Buffered geometry returned. Now getting features..." + layer);
      queryTask.ExecuteAsync(query, searchDistance);
    }

    private void AddLineSegmentToCache(ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection, double x, double y, double searchDistance, ref Int32 id)
    {
      double distance;
      ESRI.ArcGIS.Client.Geometry.MapPoint lastPoint = null;
      ESRI.ArcGIS.Client.Geometry.MapPoint originPoint = new ESRI.ArcGIS.Client.Geometry.MapPoint(x, y);
      foreach (ESRI.ArcGIS.Client.Geometry.MapPoint featurePoint in pointCollection)
      {
        if (lastPoint != null)
        {
          ESRI.ArcGIS.Client.Geometry.Polyline snapLine = GeometryUtil.Line(lastPoint, featurePoint);
          if (GeometryUtil.FindPerpendicularDistance(snapLine, originPoint, out distance))
            if (distance < searchDistance) 
              _snapObjects[id++] = snapLine;
        }
        lastPoint = new ESRI.ArcGIS.Client.Geometry.MapPoint(featurePoint.X, featurePoint.Y);
      }
    }

    private void QueryTask_ExecuteCompleted(object sender, QueryEventArgs args)
    {
      if (_originPoint == null)
        return;

      double searchDistance = (double) args.UserState;
      double x = _originPoint.X;
      double y = _originPoint.Y;

      Int32 id = 0;
      if (args.FeatureSet.Features.Count > 0)
        foreach (Graphic feature in args.FeatureSet.Features)
        {
          // test type of feature, and test it's end points.

          if (feature.Geometry is ESRI.ArcGIS.Client.Geometry.Polygon)
          {
            ESRI.ArcGIS.Client.Geometry.Polygon featurePolygon = feature.Geometry as ESRI.ArcGIS.Client.Geometry.Polygon;
            foreach (ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection in featurePolygon.Rings)
              AddLineSegmentToCache(pointCollection, x, y, searchDistance, ref id);
          }
          else if (feature.Geometry is ESRI.ArcGIS.Client.Geometry.Polyline)
          {
            ESRI.ArcGIS.Client.Geometry.Polyline featurePolyline = feature.Geometry as ESRI.ArcGIS.Client.Geometry.Polyline;
            foreach (ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection in featurePolyline.Paths)
              AddLineSegmentToCache(pointCollection, x, y, searchDistance, ref id);
          }
          else if (feature.Geometry is ESRI.ArcGIS.Client.Geometry.MapPoint)
          {
            ESRI.ArcGIS.Client.Geometry.MapPoint featurePoint = feature.Geometry as ESRI.ArcGIS.Client.Geometry.MapPoint;
            double distance = GeometryUtil.LineLength(x, y, featurePoint);
            if (distance < searchDistance)
              _snapObjects[id++] = feature.Geometry;
          }
        }
      System.Diagnostics.Debug.WriteLine("Async: Number of objects cached: " + args.FeatureSet.Features.Count.ToString());

      CalculateAndAddLineGraphics();  // We need to redraw, so we get the right snap marker
    }

    private void CancelScaleRotate()
    {
      bool redraw = _srPoint != null;

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;

      _srPoint = null;     // Stop mouse move from working;
      _oldRotation = parcelData.ScaleValue;
      _srSnapPointId = -1;

      if (redraw)
        CalculateAndAddLineGraphics();  // Redraw so we don't have snap graphic shown
    }

    double _oldRotation = 0;
    double _oldScale = 1;
    double _moveScale = 0;
    double _moveRotation = 1;
    private void ParcelMap_MouseMove(object sender, MouseEventArgs e)
    {
      if (ParcelLineInfoWindow.IsOpen == true &&
          ParcelLineInfoWindow.Visibility == System.Windows.Visibility.Visible)
      {
        const double hideDistance = 25;
        double width = ParcelLineInfoWindow.ActualWidth;
        double height = ParcelLineInfoWindow.ActualHeight;

        var anchorScreenPoint = ParcelMap.MapToScreen(ParcelLineInfoWindow.Anchor);
        double x1 = anchorScreenPoint.X - width/2 - hideDistance;
        double y1 = anchorScreenPoint.Y - height - hideDistance - 10; // -ve for info indicator
        double x2 = anchorScreenPoint.X + width/2 + hideDistance;
        double y2 = anchorScreenPoint.Y + hideDistance;
        var envelope = new ESRI.ArcGIS.Client.Geometry.Envelope(x1, y1, x2, y2);

        Point pointLoc = e.GetPosition(this);
        if (!envelope.Intersects(new ESRI.ArcGIS.Client.Geometry.Envelope(pointLoc.X, pointLoc.Y, pointLoc.X, pointLoc.Y)))
        {
          ParcelLineInfoWindow.IsOpen = false;
          ParcelMap.Focus();  // Cause any non-committed cell in the popup window to lose its focus. This will commit the cell.

        }
      }

      if ((_srPoint == null) || !PDE_Tools.IsExpanded)
        return;

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;

      ESRI.ArcGIS.Client.Geometry.MapPoint currentPoint = ParcelMap.ScreenToMap(e.GetPosition(this));

      if (RotationButton.IsChecked == true)
      {
        double rotation = GeometryUtil.Angle(_srPoint, currentPoint, _originPoint) + _oldRotation;
        while (rotation < -Math.PI)
          rotation += Math.PI * 2;
        while (rotation > Math.PI)
          rotation -= Math.PI * 2;

        parcelData.RotationValue = rotation;
      }
      else if (ScaleButton.IsChecked == true)
      {
        parcelData.ScaleValue = GeometryUtil.Scale(_srPoint, currentPoint, _originPoint) * _oldScale;
      }

      // If we have a snap point, adjust scale/rotation if we can snap point.
      if (_srSnapPointId != -1)
      {
        bool isRotating = RotationButton.IsChecked.GetValueOrDefault(false);
        bool isScaling = ScaleButton.IsChecked.GetValueOrDefault(false);

        double distanceToPoint = _srDistanceToPoint * parcelData.ScaleValue;
        double bearingToPoint = _srBearingToPoint - parcelData.RotationValue;
        if (bearingToPoint >= 2 * Math.PI)
          bearingToPoint -= 2 * Math.PI;

        ESRI.ArcGIS.Client.Geometry.MapPoint snapPointSR = GeometryUtil.ConstructPoint(_originPoint, bearingToPoint, distanceToPoint);
        if (snapPointSR != null)
        {
          ESRI.ArcGIS.Client.Geometry.Polyline snapLine;
          SnapPointToCacheObjects(snapPointSR, isScaling, out snapLine);  // if scaling, skip zero distance so  
          if (snapLine != null)                                           // we don't snap to origin point.
          {
            bool ok = false;
            ESRI.ArcGIS.Client.Geometry.MapPoint intersectPoint = null;
            if (isRotating)
            {
              ok = GeometryUtil.ConstructPointLineCurveIntersection(snapLine, _originPoint, bearingToPoint, distanceToPoint, out intersectPoint);  // distanceToPoint is radius here
              if (ok) // Only snap if the mouse location is within snap solution
                ok = GeometryUtil.LineLength(intersectPoint, currentPoint) <= _xmlConfiguation.SnapTolerance;
              if (ok)
                parcelData.RotationValue = GeometryUtil.Angle(_srSnapPoint, intersectPoint, _originPoint);
            }
            else if (isScaling)
            {
              ESRI.ArcGIS.Client.Geometry.MapPoint endPoint = GeometryUtil.ConstructPoint(_originPoint, bearingToPoint, distanceToPoint + _xmlConfiguation.SnapTolerance);
              ESRI.ArcGIS.Client.Geometry.Polyline sourceLine = GeometryUtil.Line(_originPoint, endPoint);
              ok = GeometryUtil.ConstructPointLineLineIntersection(snapLine, sourceLine, out intersectPoint);
              if (ok) // Only snap if the mouse location is within snap solution
                ok = GeometryUtil.LineLength(intersectPoint, currentPoint) <= _xmlConfiguation.SnapTolerance;
              if (ok)
              {
                double scale = GeometryUtil.Scale(_srSnapPoint, intersectPoint, _originPoint);
                if (scale > 0.0)
                  parcelData.ScaleValue = scale;
              }
            }

            // Test code for debugging.
            //
            //GraphicsLayer testGraphicsLayer = ParcelMap.Layers["TestGraphicLayer"] as GraphicsLayer;
            //testGraphicsLayer.ClearGraphics();
            //if (intersectPoint != null)
            //{
            //  ESRI.ArcGIS.Client.Graphic graphic = new ESRI.ArcGIS.Client.Graphic()
            //  {
            //    Geometry = intersectPoint,
            //    Symbol = LayoutRoot.Resources["TestMarkerSymbol"] as ESRI.ArcGIS.Client.Symbols.Symbol
            //  };
            //  testGraphicsLayer.Graphics.Add(graphic);
            //}
          }
        }
      }

      // Only redraw if there have been an update;
      // Otherwise runtime does not process mouse up and over flashes.
      if ((parcelData.ScaleValue != _moveScale) || (parcelData.RotationValue != _moveRotation))
      {
        CalculateAndAddLineGraphics();
        _moveScale = parcelData.ScaleValue;
        _moveRotation = parcelData.RotationValue;
      }
    }

    private bool SnapPointToCacheObjects(ESRI.ArcGIS.Client.Geometry.MapPoint point, bool skipZeroDistance, out ESRI.ArcGIS.Client.Geometry.Polyline snapLine)
    {
      snapLine = null; // default return arg.
      ESRI.ArcGIS.Client.Geometry.Polyline bestSnapLine = null;
      double shortestDistance = double.MaxValue;

      Int32 oid = -1;

      foreach (KeyValuePair<Int32, ESRI.ArcGIS.Client.Geometry.Geometry> kvp in _snapObjects)
      {
        if (-1 == kvp.Key)  // this should not happen
          continue;

        ESRI.ArcGIS.Client.Geometry.Polyline line = kvp.Value as ESRI.ArcGIS.Client.Geometry.Polyline;
        if (line != null)
        {
          ESRI.ArcGIS.Client.Geometry.PointCollection pathPoints = line.Paths.First();

          double distance;
          if (GeometryUtil.FindPerpendicularDistance(line, point, out distance) && (distance <= _xmlConfiguation.SnapTolerance))
          {
            if ((distance < shortestDistance) && (!(skipZeroDistance && (distance == 0))))
            {
              oid = kvp.Key;
              bestSnapLine = line;
              shortestDistance = distance;
            }
          }
        }

        /* For now we don't snap to points
         * This does not play well with scale or rotate on its own, as
         * it will want to pull the geometry in the other action (rotate or scale) also.
         * 
        ESRI.ArcGIS.Client.Geometry.MapPoint cachePoint = kvp.Value as ESRI.ArcGIS.Client.Geometry.MapPoint;
        if (cachePoint != null)
        {
          double distance = GeometryUtil.LineLength(cachePoint, point);
          if ((distance < shortestDistance) && (!(skipZeroDistance && (distance == 0))))
          {
            oid = kvp.Key;
            bestSnapLine = null;
            shortestDistance = distance;
          }
        }
        */
      }

      if (oid != -1)
      {
        snapLine = bestSnapLine;
        return true;
      }
      return false;
    }

    private void ResetRotationScale()
    {
      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;

      parcelData.RotationValue = 0.0;
      parcelData.ScaleValue = 1.0;
    }

    private void Rotation_KeyUp(object sender, KeyEventArgs e)
    {
      if ((e.Key == Key.Enter) || (e.Key == Key.Tab))
        Rotation_LostFocus(sender, null);
    }

    private void Rotation_LostFocus(object sender, RoutedEventArgs e)
    {
      BindingExpression bindingExp = RotationText.GetBindingExpression(TextBox.TextProperty);
      bindingExp.UpdateSource();

      CalculateAndAddLineGraphics();
    }

    private void Scale_KeyUp(object sender, KeyEventArgs e)
    {
      if ((e.Key == Key.Enter) || (e.Key == Key.Tab))
        Scale_LostFocus(sender, null);
    }

    private void Scale_LostFocus(object sender, RoutedEventArgs e)
    {
      BindingExpression bindingExp = ScaleText.GetBindingExpression(TextBox.TextProperty);
      bindingExp.UpdateSource();

      CalculateAndAddLineGraphics();
    }

    private void ToolBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      // Hide the right button (overflow) icon in dialog control. XAML code is too much
      // http://stackoverflow.com/questions/4662428/how-to-hide-arrow-on-right-side-of-a-toolbar

      ToolBar toolBar = sender as ToolBar;
      FrameworkElement overflowGrid = toolBar.Template.FindName("OverflowGrid", toolBar) as FrameworkElement;
      if (overflowGrid != null)
        overflowGrid.Visibility = toolBar.HasOverflowItems ? Visibility.Visible : Visibility.Collapsed;

      FrameworkElement mainPanelBorder = toolBar.Template.FindName("MainPanelBorder", toolBar) as FrameworkElement;
      if (mainPanelBorder != null)
      {
        var defaultMargin = new Thickness(0, 0, 11, 0);
        mainPanelBorder.Margin = toolBar.HasOverflowItems ? defaultMargin : new Thickness(0);
      }
    }
  }
}
