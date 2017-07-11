using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ESRI.ArcGIS.Client.Tasks;

namespace Utilities
{
  // These enums match ESRI implementation 1:1 (only add to end with a large offset)

  public enum esriCadastralAreaUnits 
  {
      esriCAUImperial = 0,
      esriCAUMetric = 1,
      esriCAUSquareMeter = 2,
      esriCAUHectare = 3,
      esriCAUAcre = 4,
      esriCAUSquareRods = 5,
      esriCAURoods = 6,
      esriCAUPerches = 7,
      esriCAUSquareFoot = 8,
      esriCAUSquareUSFoot = 9,
      esriCAUQuarterSections = 10,
      esriCAUSections = 11
  };

  public enum esriCadastralDistanceUnits
  {
      eUnknown = -1,
      eMeters = 0,
      eMillimeters,
      eCentimeters,
      eKilometers,
      eFeet,
      eYards,
      eInches,
      eMiles,
      eChains,
      eLinks,
      eRods,
      eSurveyFeet,
      eSurveyYards,
      eSurveyMiles,
      eSurveyChains,
      eSurveyLinks,
      eSurveyRods,
      eRomanMiles,
      eNauticalMiles
  }

  static class GeometryUtil
  {
    // This gives an angle b/t 3 points returning a result b/t -PI to +PI
    //
    static public double Angle(ESRI.ArcGIS.Client.Geometry.MapPoint p0,   // Point 1
                         ESRI.ArcGIS.Client.Geometry.MapPoint p1,   // Point 2
                         ESRI.ArcGIS.Client.Geometry.MapPoint c)    // Center Point
    {
      return Math.Atan2(p1.Y - c.Y, p1.X - c.X) - Math.Atan2(p0.Y - c.Y, p0.X - c.X);
    }

    static public double Scale(ESRI.ArcGIS.Client.Geometry.MapPoint p0,   // Point 1
                     ESRI.ArcGIS.Client.Geometry.MapPoint p1,   // Point 2
                     ESRI.ArcGIS.Client.Geometry.MapPoint c)    // Center Point
    {
      return LineLength(c, p1) / LineLength(c, p0);
    }

    static public double LineLength(ESRI.ArcGIS.Client.Geometry.MapPoint p1, ESRI.ArcGIS.Client.Geometry.MapPoint p2)
    {
      return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    static public double LineLength(double x, double y, ESRI.ArcGIS.Client.Geometry.MapPoint p2)
    {
      return Math.Sqrt(Math.Pow(x - p2.X, 2) + Math.Pow(y - p2.Y, 2));
    }

    static public double LineLength(double x1, double y1, double x2, double y2)
    {
      return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
    }

    static public ESRI.ArcGIS.Client.Geometry.MapPoint ConstructPoint(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, double bearing, double distance)
    {
      if ((distance == 0) || (startPoint == null))
        return null;

      double startX = startPoint.X;
      double startY = startPoint.Y;
      double angle = Math.PI / 2 - bearing;

      double endX = startX + distance * (float)Math.Cos(angle);
      double endY = startY + distance * (float)Math.Sin(angle);

      if (double.IsNaN(endX) || double.IsNaN(endY))
        return null;

      return new ESRI.ArcGIS.Client.Geometry.MapPoint(endX, endY);
    }

    static public ESRI.ArcGIS.Client.Geometry.Polyline Line(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, double bearing, double distance, out ESRI.ArcGIS.Client.Geometry.MapPoint endPoint)
    {
      endPoint = null;
      if (distance == 0)
        return null;

      endPoint = ConstructPoint(startPoint, bearing, distance);

      ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection = new ESRI.ArcGIS.Client.Geometry.PointCollection();

      pointCollection.Add(startPoint);
      pointCollection.Add(endPoint);

      ESRI.ArcGIS.Client.Geometry.Polyline polyline = new ESRI.ArcGIS.Client.Geometry.Polyline();
      polyline.Paths.Add(pointCollection);

      return polyline;
    }

    static public ESRI.ArcGIS.Client.Geometry.Polyline Line(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, ESRI.ArcGIS.Client.Geometry.MapPoint endPoint)
    {
      ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection = new ESRI.ArcGIS.Client.Geometry.PointCollection();

      pointCollection.Add(startPoint);
      pointCollection.Add(endPoint);

      ESRI.ArcGIS.Client.Geometry.Polyline polyline = new ESRI.ArcGIS.Client.Geometry.Polyline();
      polyline.Paths.Add(pointCollection);

      return polyline;
    }

    static public ESRI.ArcGIS.Client.Geometry.Polyline ConstructArcSegment(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, double bearing, double distance, double radius, bool isMinor, SweepDirection direction, out ESRI.ArcGIS.Client.Geometry.MapPoint endPoint)
    {
      endPoint = null;
      if (distance == 0)
        return null;

      endPoint = ConstructPoint(startPoint, bearing, distance);
      if (endPoint == null)
        return null;

      return ConstructArcSegment(startPoint, endPoint, radius, isMinor, direction);
    }

    // Create an ESRI polyline based on a densified representation of WPFs ArcSegment
    static public ESRI.ArcGIS.Client.Geometry.Polyline ConstructArcSegment(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, ESRI.ArcGIS.Client.Geometry.MapPoint endPoint, double radius, bool isMinor, SweepDirection direction)
    {
      if (endPoint == null)
        return null;

      // WPF ArcSegment has issue with high coordinates values.  
      // Bring coordinates down to 0,0 and translate back to real world later.

      double startX = startPoint.X;
      double startY = startPoint.Y;

      double absRadius = Math.Abs(radius);

      // We need to switch the curve direction, b/c we are starting with the end point
      if (radius > 0)
        direction = direction == SweepDirection.Clockwise ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

      Size radiusAspect = new Size(absRadius, absRadius);
      Point myEndPoint = new Point(endPoint.X - startX, endPoint.Y - startY);

      bool isLargeArc = !isMinor;
      ArcSegment wpfArcSegment = new ArcSegment(myEndPoint, radiusAspect, 0, isLargeArc, direction, false);

      // compose one or more segments into a collection 
      var pathcoll = new PathSegmentCollection();
      pathcoll.Add(wpfArcSegment);

      // create a figure based on the set of segments 
      var pathFigure = new PathFigure();
      pathFigure.Segments = pathcoll;
      pathFigure.IsClosed = false;

      // compose a collection of figures 
      var figureCollection = new PathFigureCollection();
      figureCollection.Add(pathFigure);

      // create a path-geometry using the figures collection 
      var geometryPath = new PathGeometry(figureCollection);

      ESRI.ArcGIS.Client.Geometry.PointCollection pointCollection = new ESRI.ArcGIS.Client.Geometry.PointCollection();

      double numSegments = 1.0 / 50;        // Default 50
      Point point, tangent;

      Point pointA, pointB;
      geometryPath.GetPointAtFractionLength(0, out pointA, out tangent);
      geometryPath.GetPointAtFractionLength(numSegments, out pointB, out tangent);
      double partDistance = LineLength(pointA.X, pointA.Y, pointB.X, pointB.Y);
      if (partDistance > 1.0)
        numSegments /= partDistance;
      if (1 / numSegments > 160)            // cap it at 160 vertexes 
        numSegments = 1.0 / 160;            // Server is having issue with 185+ vertexes (180 seems ok)

      for (double fraction = 0.0; fraction < 1.0; fraction += numSegments)
      {
        geometryPath.GetPointAtFractionLength(fraction, out point, out tangent);
        pointCollection.Add(new ESRI.ArcGIS.Client.Geometry.MapPoint(point.X + startX, point.Y + startY));
      }
      pointCollection.Add(endPoint);  // faction 1 can be skipped, so add it here.

      ESRI.ArcGIS.Client.Geometry.Polyline polyline = new ESRI.ArcGIS.Client.Geometry.Polyline();
      polyline.Paths.Add(pointCollection);

      return polyline;
    }

    static public ESRI.ArcGIS.Client.Geometry.MapPoint ConstructCenterPoint(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, double bearing, double distance, double radius, bool isMinor, SweepDirection direction, out double bearing1, out double bearing2)
    {
      bearing1 = bearing2 = 0.0;
      if (distance == 0)
        return null;

      ESRI.ArcGIS.Client.Geometry.MapPoint endPoint = ConstructPoint(startPoint, bearing, distance);
      if (endPoint == null)
        return null;
      return ConstructCenterPoint(startPoint, endPoint, radius, isMinor, direction, out bearing1, out bearing2);
    }

    static public ESRI.ArcGIS.Client.Geometry.MapPoint ConstructCenterPoint(ESRI.ArcGIS.Client.Geometry.MapPoint startPoint, ESRI.ArcGIS.Client.Geometry.MapPoint endPoint, double radius, bool isMinor, SweepDirection direction, out double bearing1, out double bearing2)
    {
      bearing1 = bearing2 = 0.0;

      // We need to switch the curve direction, b/c we are starting with the end point
      if (radius > 0)
        direction = direction == SweepDirection.Clockwise ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

      bool isLargeArc = !isMinor;

      // Used logic from http://www.charlespetzold.com/blog/2008/01/Mathematics-of-ArcSegment.html

      Point pt1 = new Point(startPoint.X, startPoint.Y);
      Point pt2 = new Point(endPoint.X, endPoint.Y);
      bool isCounterclockwise = direction == SweepDirection.Counterclockwise;

      // Get info about chord that connects both points
      Point midPoint = new Point((pt1.X + pt2.X) / 2, (pt1.Y + pt2.Y) / 2);
      Vector vect = pt2 - pt1;
      double halfChord = vect.Length / 2;

      // Get vector from chord to center
      Vector vectRotated;

      // (comparing two Booleans here!)
      if (isLargeArc == isCounterclockwise)
        vectRotated = new Vector(-vect.Y, vect.X);
      else
        vectRotated = new Vector(vect.Y, -vect.X);

      vectRotated.Normalize();   // maintains its direction but its Length becomes 1.

      // Distance from chord to center 
      double centerDistance = Math.Sqrt(Math.Abs(radius * radius - halfChord * halfChord));

      // Calculate center point
      Point center = midPoint + centerDistance * vectRotated;

      if ((center.X == double.NaN) || (center.Y == double.NaN))
        return null;

      // Get angles from center to the two points
      double angle1 = Math.Atan2(pt1.Y - center.Y, pt1.X - center.X);
      double angle2 = Math.Atan2(pt2.Y - center.Y, pt2.X - center.X);

      if ((angle1 == double.NaN) || (angle2 == double.NaN))
        return null;

      // (another comparison of two Booleans!)
      if (isLargeArc == (Math.Abs(angle2 - angle1) < Math.PI))
      {
        if (angle1 < angle2)
          angle1 += 2 * Math.PI;
        else
          angle2 += 2 * Math.PI;
      }

      // Convert to bearing and reverse line
      bearing1 = (Math.PI / 2 - angle1) - Math.PI;
      if (bearing1 < 0)
        bearing1 += Math.PI * 2;

      // Convert to bearing and reverse line
      bearing2 = (Math.PI / 2 - angle2) - Math.PI;
      if (bearing2 < 0)
        bearing2 += Math.PI * 2;

      return new ESRI.ArcGIS.Client.Geometry.MapPoint(center.X, center.Y);
    }

    static public double DegreeToRadian(double angle)
    {
      return Math.PI * angle / 180.0;
    }

    static public double RadianToDegree(double angle)
    {
      return angle * (180.0 / Math.PI);
    }

    static public bool CalculateArcLength(double radius, double arcLength, out double chordLength, out bool isMinor)
    {
      if ((arcLength == 0) || (radius == 0))
      {
        chordLength = arcLength;
        isMinor = true;
        return false;
      }

      double circumference = 2 * Math.PI * Math.Abs(radius);
      double angleOfChord = arcLength / circumference * (Math.PI * 2);

      chordLength = radius * Math.Sin(angleOfChord / 2) * 2;
      if (chordLength < 0)
        chordLength = -chordLength;

      isMinor = angleOfChord < Math.PI;

      if (angleOfChord > Math.PI * 2)
      {
        chordLength = arcLength;
        isMinor = true;
        return false;
      }

      return true;
    }

    static public bool FindPerpendicularDistance(ESRI.ArcGIS.Client.Geometry.Polyline srcLine, ESRI.ArcGIS.Client.Geometry.MapPoint srcPoint, out double perpendicularDistance)
    {
      // Given a straight line geometry and a point, check whether a perpendicular can be drawn
      // from the line to the point and if so, calculate the distance.

      // If the line was purely in the x direction, this would be a trivial task:
      //   i) If the x ordinate of the point is with the x range of the line a perpendicular can be drawn
      //  ii) If i) is true, the distance is just the difference in the y values of the line and the point.

      // For any general line orientation and point location, we can make it look like the simple case
      // above by:
      //   i) Translating the point and line such that the 'from' end of the line is at the origin
      //  ii) Rotating the point and line about the origin so that the line is indeed purely in the 
      //      x direction.
      // iii) Check the x ordinate of the point against the line's xMin and xMax.
      //  iv) The magnitude of the point's y value is the distance.

      perpendicularDistance = -1.0;

      if ((srcLine == null) || (srcPoint == null))
        return false;

      // Check if line is 2 point.
      if (srcLine.Paths.Count == 0)
        return false;

      // For now we are only going to look at the first segment.
      ESRI.ArcGIS.Client.Geometry.PointCollection pathPoints = srcLine.Paths.First();
      Int32 pointCount = pathPoints.Count;
      if (pointCount < 1)
        return false;

      double pointX = srcPoint.X;
      double pointY = srcPoint.Y;

      ESRI.ArcGIS.Client.Geometry.MapPoint fromPoint = pathPoints[0];
      ESRI.ArcGIS.Client.Geometry.MapPoint toPoint = pathPoints[pointCount - 1];
      if ((fromPoint == null) || (toPoint == null))
        return false;

      double fromX = fromPoint.X;
      double fromY = fromPoint.Y;
      double toX = toPoint.X;
      double toY = toPoint.Y;

      // Translate the coordinates to place the from point at the origin.
      pointX -= fromX;
      pointY -= fromY;
      toX -= fromX;
      toY -= fromY;

      if ((toX == 0.0) && (toY == 0.0))
        return false;

      double angle = Math.Atan2(toY, toX);

      // Rotate the to point and the test point 
      // (not require, since we have rotated the line flat)
      //
      // x' =  x cos(angle) + y sin(angle)
      // y' =  y cos(angle) - x sin(angle)

      double cosAngle = Math.Cos(angle);
      double sinAngle = Math.Sin(angle);

      double rotToX = toX * cosAngle + toY * sinAngle;

      // No need to compute the to point rotated y position
      // It will just be zero.
      // double rotToY = toY * cosAngle - toX * sinAngle;

      double rotPointX = pointX * cosAngle + pointY * sinAngle;
      // Check whether the point is with the bounds of the line
      if ((rotPointX < 0.0) || (rotPointX > rotToX))
        return false;

      double rotPointY = pointY * cosAngle - pointX * sinAngle;
      if (rotPointY < 0)
        perpendicularDistance = -1.0 * rotPointY;
      else
        perpendicularDistance = rotPointY;

      return true;
    }

    static public bool ConstructPointLineCurveIntersection(ESRI.ArcGIS.Client.Geometry.Polyline srcLine, ESRI.ArcGIS.Client.Geometry.MapPoint centerPoint, double bearing, double radius, out ESRI.ArcGIS.Client.Geometry.MapPoint intersectPoint)
    {
      // Given a straight line geometry and a point, find the intersection point.

      // If the line was purely in the x direction, this would be a trivial task:
      //   i) If the x ordinate of the point is with the x range of the line a perpendicular can be drawn
      //  ii) If i) is true, the distance is just the difference in the y values of the line and the point.
      // iii) Using a triangle (perpendicular distance, radius and angle) we can calculate point along line

      // For any general line orientation and point location, we can make it look like the simple case
      // above by:
      //   i) Translating the point and line such that the 'from' end of the line is at the origin
      //  ii) Rotating the point and line about the origin so that the line is indeed purely in the 
      //      x direction.
      // iii) Check the x ordinate of the point against the line's xMin and xMax.
      //  iv) The magnitude of the point's y value is the distance.

      intersectPoint = null;
      if ((srcLine == null) || (centerPoint == null))
        return false;

      // Check if line is 2 point.
      if (srcLine.Paths.Count == 0)
        return false;

      // For now we are only going to look at the first segment.
      ESRI.ArcGIS.Client.Geometry.PointCollection pathPoints = srcLine.Paths.First();
      Int32 pointCount = pathPoints.Count;
      if (pointCount < 1)
        return false;

      double srcAngle = Math.PI / 2 - bearing;
      ESRI.ArcGIS.Client.Geometry.MapPoint endPoint = ConstructPoint(centerPoint, bearing, radius);

      double endPointX = endPoint.X;
      double endPointY = endPoint.Y;

      double centerPointX = centerPoint.X;
      double centerPointY = centerPoint.Y;

      ESRI.ArcGIS.Client.Geometry.MapPoint fromPoint = pathPoints[0];
      ESRI.ArcGIS.Client.Geometry.MapPoint toPoint = pathPoints[pointCount - 1];
      if ((fromPoint == null) || (toPoint == null))
        return false;

      double fromX = fromPoint.X;
      double fromY = fromPoint.Y;
      double toX = toPoint.X;
      double toY = toPoint.Y;

      // Translate the coordinates to place the from point at the origin.
      endPointX -= fromX;
      endPointY -= fromY;
      centerPointX -= fromX;
      centerPointY -= fromY;
      toX -= fromX;
      toY -= fromY;

      if ((toX == 0.0) && (toY == 0.0))
        return false;

      double lineAngle = Math.Atan2(toY, toX);

      // Rotate the to point and the test point (clockwise)
      // (not require, since we have rotated the line flat)
      // x' =  x.cos(angle) + y.sin(angle)
      // y' =  y.cos(angle) - x.sin(angle)

      double cosAngle = Math.Cos(lineAngle);
      double sinAngle = Math.Sin(lineAngle);

      double rotToX = toX * cosAngle + toY * sinAngle;

      // No need to compute the to point rotated y position. It will just be zero.
      // double rotToY = toY * cosAngle - toX * sinAngle;

      double rotEndPointX = endPointX * cosAngle + endPointY * sinAngle;
      // Check whether the point is with the bounds of the line
      if ((rotEndPointX <= 0.0) || (rotEndPointX >= rotToX))
        return false;
      double rotEndPointY = endPointY * cosAngle - endPointX * sinAngle;
      double endPointPerpendicularDistance = Math.Abs(rotEndPointY);

      double rotCenterPointX = centerPointX * cosAngle + centerPointY * sinAngle;
      // Check whether the point is with the bounds of the line
      double rotCenterPointY = centerPointY * cosAngle - centerPointX * sinAngle;
      double centerPointPerpendicularDistance = Math.Abs(rotCenterPointY);

      //                                                                   _____
      // Get distance b/t rotCenterPointY and arc intersection point: k = √r²-d²
      double k = Math.Sqrt(Math.Pow(radius, 2) - Math.Pow(centerPointPerpendicularDistance, 2));

      // For the first quadrant we have a good solution. The others we need to decide which
      // way to go on the line, and which origin point to use. The simplest solution is to
      // calculate both solutions and choose the closest.
      // 
      // Use the following matrix to rotate solution point back counterclockwise.
      // x' =  x.cos(angle) + y.sin(angle)
      // y' = -y.cos(angle) + x.sin(angle)
      //
      // y=0 since the line is horizontal.
      // rotBackIntersectionPointX = intersectionDistance * cosAngle + 0 * sinAngle;
      // rotBackIntersectionPointY = -0 * cosAngle + intersectionDistance * sinAngle;
      //
      // To complete the solution, add the original offset (fromX, fromY)

      // Intersection point (solution 1)
      double intersectionDistance = rotCenterPointX + k;
      ESRI.ArcGIS.Client.Geometry.MapPoint intersectPoint1 = null;
      if ((intersectionDistance >= 0) || (intersectionDistance <= rotToX))
      {
        double rotBackIntersectionPointX = intersectionDistance * cosAngle + fromX;
        double rotBackIntersectionPointY = intersectionDistance * sinAngle + fromY;
        intersectPoint1 = new ESRI.ArcGIS.Client.Geometry.MapPoint(rotBackIntersectionPointX, rotBackIntersectionPointY);
      }

      // Intersection point (solution 2)
      intersectionDistance = rotCenterPointX - k;
      ESRI.ArcGIS.Client.Geometry.MapPoint intersectPoint2 = null;
      if ((intersectionDistance >= 0) || (intersectionDistance <= rotToX))
      {
        double rotBackIntersectionPointX = intersectionDistance * cosAngle + fromX;
        double rotBackIntersectionPointY = intersectionDistance * sinAngle + fromY;
        intersectPoint2 = new ESRI.ArcGIS.Client.Geometry.MapPoint(rotBackIntersectionPointX, rotBackIntersectionPointY);
      }

      // Choose the solution that's closest to our source point.
      if ((intersectPoint1 != null) && (intersectPoint2 == null))
      {
        intersectPoint = intersectPoint1;
      }
      else if ((intersectPoint1 == null) && (intersectPoint2 != null))
      {
        intersectPoint = intersectPoint2;
      }
      else if ((intersectPoint1 != null) && (intersectPoint2 != null))
      {
        double distance1 = LineLength(intersectPoint1, endPoint);
        double distance2 = LineLength(intersectPoint2, endPoint);
        if (distance1 <= distance2)
          intersectPoint = intersectPoint1;  // if equal, this will also be the solution (quadrant 1)
        else
          intersectPoint = intersectPoint2;
      }
      else
        return false;

      return true;
    }

    static public bool ConstructPointLineLineIntersection(ESRI.ArcGIS.Client.Geometry.Polyline line1, ESRI.ArcGIS.Client.Geometry.Polyline line2, out ESRI.ArcGIS.Client.Geometry.MapPoint intersectionPoint)
    {
      intersectionPoint = null;
      if ((line1 == null) || (line2 == null))
        return false;

      // Check if line is 2 point.
      if ((line1.Paths.Count == 0) || (line2.Paths.Count == 0))
        return false;

      // For now we are only going to look at the first segment.
      ESRI.ArcGIS.Client.Geometry.PointCollection pathPoints1 = line1.Paths.First();
      ESRI.ArcGIS.Client.Geometry.PointCollection pathPoints2 = line2.Paths.First();
      Int32 pathPoint1Count = pathPoints1.Count;
      Int32 pathPoint2Count = pathPoints2.Count;
      if ((pathPoint1Count < 1) || (pathPoint2Count < 1))
        return false;

      // Used the following http://en.wikipedia.org/wiki/Line-line_intersection

      ESRI.ArcGIS.Client.Geometry.MapPoint point1f = pathPoints1[0];
      ESRI.ArcGIS.Client.Geometry.MapPoint point1t = pathPoints1[pathPoint1Count - 1];
      ESRI.ArcGIS.Client.Geometry.MapPoint point2f = pathPoints2[0];
      ESRI.ArcGIS.Client.Geometry.MapPoint point2t = pathPoints2[pathPoint2Count - 1];

      double x1 = point1f.X; double y1 = point1f.Y;
      double x2 = point1t.X; double y2 = point1t.Y;
      double x3 = point2f.X; double y3 = point2f.Y;
      double x4 = point2t.X; double y4 = point2t.Y;

      double divider = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
      double intersectX = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / divider;
      double intersectY = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / divider;

      // Now that we have intersection line, lay the source line horizontal (either would do)
      // and rotation intersection point on that. From this we can test if the line is within.

      // Shift coordinates to 0,0
      double x2shift = x2 - x1;
      double y2shift = y2 - y1;
      double intersectXshift = intersectX - x1;
      double intersectYshift = intersectY - y1;

      double lineAngle = Math.Atan2(y2shift, x2shift);

      // Rotate the to point and the test point (clockwise)
      // x' =  x.cos(angle) + y.sin(angle)
      // y' =  y.cos(angle) - x.sin(angle)

      double cosAngle = Math.Cos(lineAngle);
      double sinAngle = Math.Sin(lineAngle);

      double rotX2 = x2shift * cosAngle + y2shift * sinAngle;

      // No need to compute the to point rotated y position. It will just be zero.
      // double rotY2 = y2shift * cosAngle - x2shift * sinAngle;

      double rotXIntersection = intersectXshift * cosAngle + intersectYshift * sinAngle;
      double rotYIntersection = intersectYshift * cosAngle - intersectXshift * sinAngle;

      double selfIntersectTolerance = 0.01;  // don't allow an intersection result to select start/end points
      if ((rotXIntersection < selfIntersectTolerance) || (rotXIntersection > rotX2 - selfIntersectTolerance))
        return false;

      intersectionPoint = new ESRI.ArcGIS.Client.Geometry.MapPoint(intersectX, intersectY);

      return true;
    }

    static public esriUnits UnitEsri(string sourceUnit)
    {
      switch (sourceUnit)
      {
        case "esriCentimeters": return esriUnits.esriCentimeters;
        case "esriFeet": return esriUnits.esriFeet;
        case "esriInches": return esriUnits.esriInches;
        case "esriKilometers": return esriUnits.esriKilometers;
        case "esriMeters": return esriUnits.esriMeters;
        case "esriMiles": return esriUnits.esriMiles;
        case "esriMillimeters": return esriUnits.esriMillimeters;
        case "esriNauticalMiles": return esriUnits.esriNauticalMiles;
        case "esriYards": return esriUnits.esriYards;
        default:
          return esriUnits.esriUnknownUnits;
      }
    }

    static public bool UnitFactor(string sourceUnit, out double factor)
    {
      esriUnits esriUnit = UnitEsri(sourceUnit);
      if (esriUnit != esriUnits.esriUnknownUnits)
        return UnitFactor(esriUnit, out factor);

      switch (sourceUnit.ToLower())
      {
        case "foot": 
        case "feet": 
          factor = UnitFactor(esriCadastralDistanceUnits.eFeet);
          return true;

        case "foot_us":
        case "feet_us":
          factor = UnitFactor(esriCadastralDistanceUnits.eSurveyFeet);
          return true;

        case "meter":
        case "metre":
          factor = UnitFactor(esriCadastralDistanceUnits.eMeters);
          return true;
      }

      factor = 1.0;
      return false;
    }
    
    static public bool UnitFactor(esriUnits sourceUnit, out double factor)
    {
      factor = 1.0;

      switch (sourceUnit)
      {
        case esriUnits.esriCentimeters:
          factor = 0.01;
          break;

        case esriUnits.esriFeet:
          factor = 0.3048;  // International feet is 0.30480060960121919; 
          break;

        case esriUnits.esriInches:
          factor = 0.0254;
          break;

        case esriUnits.esriKilometers:
          factor = 1000.0;
          break;

        case esriUnits.esriMeters:
          factor = 1.0;
          break;

        case esriUnits.esriMiles:
          factor = 1609.344;
          break;

        case esriUnits.esriMillimeters:
          factor = 0.001;
          break;

        case esriUnits.esriNauticalMiles:
          factor = 1852.0;
          break;

        case esriUnits.esriYards:
          factor = 0.9144;
          break;

        default:
          factor = 1.0;
          return false;
      }
      return true;
    }

    static private bool IsFactor(double value, double factor, ref double epsilon)
    {
      double factorDiff = Math.Abs(value - factor);
      if (factorDiff < epsilon)
        epsilon = factorDiff;    // get the best value of all the units

      return (value-epsilon >= factor) && (value+epsilon <= factor);
    }

    static public string PacketUnitString(double value)
    {
       double epsilon = 0.0001;

       if (IsFactor(value, UnitFactor(esriCadastralDistanceUnits.eMeters), ref epsilon))
         return "meter";

       if (IsFactor(value, UnitFactor(esriCadastralDistanceUnits.eSurveyFeet), ref epsilon))
         return "foot_us";

       if (IsFactor(value, UnitFactor(esriCadastralDistanceUnits.eFeet), ref epsilon))
         return "foot";

       if (IsFactor(value, UnitFactor(esriCadastralDistanceUnits.eLinks), ref epsilon))
         return "link";

       return "meter"; // GSE packet only support the above types
    }

    static public double AsMeters(esriUnits sourceUnit, double value)
    {
      double factor = 1.0;
      UnitFactor(sourceUnit, out factor); // will return 1.0 on error.

      return factor * value;
    }

    static public string GetUnit(double distanceFactor, bool shortNotation)
    {
      double epsilon = 0.0001;

      if (!shortNotation)
      {
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eMeters), ref epsilon))
          return (string)Application.Current.FindResource("strMeters");
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eFeet), ref epsilon))
          return (string)Application.Current.FindResource("strFeet");
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eSurveyFeet), ref epsilon))
          return (string)Application.Current.FindResource("strFeetUS");
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eLinks), ref epsilon))
          return (string)Application.Current.FindResource("strLink");
      }
      else
      {
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eMeters), ref epsilon))
          return (string)Application.Current.FindResource("strMetersShort");
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eFeet), ref epsilon))
          return (string)Application.Current.FindResource("strFeetShort");
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eSurveyFeet), ref epsilon))
          return (string)Application.Current.FindResource("strFeetUSShort");
        if (IsFactor(distanceFactor, UnitFactor(esriCadastralDistanceUnits.eLinks), ref epsilon))
          return (string)Application.Current.FindResource("strLinkShort");
      }

      return "";
    }

    static public string GetUnit(esriUnits eDistanceType, bool shortNotation)
    {
      if (!shortNotation)

        switch (eDistanceType)
        {
          case esriUnits.esriMeters:
            return (string)Application.Current.FindResource("strMeters");
          case esriUnits.esriFeet:
            return (string)Application.Current.FindResource("strFeet");
        }

      else

        switch (eDistanceType)
        {
          case esriUnits.esriMeters:
            return (string)Application.Current.FindResource("strMetersShort");
          case esriUnits.esriFeet:
            return (string)Application.Current.FindResource("strFeetShort");
        }

      return "";
    }

    static public esriCadastralAreaUnits DefaultAreaUnit(double factor)
    {
      double epsilon = 0.0001;

       if (IsFactor(factor, UnitFactor(esriCadastralDistanceUnits.eMeters), ref epsilon))
         return esriCadastralAreaUnits.esriCAUSquareMeter;

       if (IsFactor(factor, UnitFactor(esriCadastralDistanceUnits.eFeet), ref epsilon))
         return esriCadastralAreaUnits.esriCAUSquareFoot;

       if (IsFactor(factor, UnitFactor(esriCadastralDistanceUnits.eSurveyFeet), ref epsilon))
         return esriCadastralAreaUnits.esriCAUSquareUSFoot;

       // TODO...

       // if (IsFactor(value, UnitFactor(esriCadastralDistanceUnits.eLinks), ref epsilon))
       // ..etc..

       return esriCadastralAreaUnits.esriCAUSquareMeter;
    }

    static public esriCadastralAreaUnits DefaultAreaUnit(esriUnits eCadastralLayerDistanceUnit)
    {
      switch (eCadastralLayerDistanceUnit)
      {
        case esriUnits.esriMeters:
          return esriCadastralAreaUnits.esriCAUSquareMeter;

        case esriUnits.esriFeet:
          return esriCadastralAreaUnits.esriCAUSquareFoot; // esriCAUAcre;

        // TODO...

        //case esriCDUUSSurveyFoot:
        //  return esriCAUAcre;
        //break;

        //// aka, default to meters for:
        //case esriCDUChain:
        //case esriCDULink:
        //case esriCDURod:
        //  return esriCadastralAreaUnits.esriCAUMetric;

        //case esriCDUUSSurveyChain:
        //case esriCDUUSSurveyLink:
        //case esriCDUUSSurveyRod:
        //    return esriCadastralAreaUnits.esriCAUAcre;
      }

      return esriCadastralAreaUnits.esriCAUSquareMeter;  // default will always catch this.
    }

    static public string GetArea(esriCadastralAreaUnits unit, bool shortNotation)
    {
      if (!shortNotation)

        switch (unit)
        {
          //case esriCadastralAreaUnits.esriCAUImperial: res_id = IDS_ACRESROODSPERCHES;

          case esriCadastralAreaUnits.esriCAUSquareMeter:
            return (string)Application.Current.FindResource("strMetric");

          case esriCadastralAreaUnits.esriCAUMetric:
            return (string)Application.Current.FindResource("strMetric");

          //TODO...
          //case esriCadastralAreaUnits.esriCAUAcre:
          //case esriCadastralAreaUnits.esriCAUHectare:res_id = IDS_HECTARES;
          //case esriCadastralAreaUnits.esriCAUPerches:res_id = IDS_PERCHES;
          //case esriCadastralAreaUnits.esriCAUQuarterSections:res_id = IDS_QUARTERSECTIONS;
          //case esriCadastralAreaUnits.esriCAURoods:res_id = IDS_ROODS;
          //case esriCadastralAreaUnits.esriCAUSections:res_id = IDS_SECTIONS;
          //case esriCadastralAreaUnits.esriCAUSquareFoot:res_id = IDS_SQFEET;
          //case esriCadastralAreaUnits.esriCAUSquareMeter:res_id = IDS_SQMETERS;
          //case esriCadastralAreaUnits.esriCAUSquareRods:res_id = IDS_SQRODS;

          case esriCadastralAreaUnits.esriCAUSquareFoot:
            return (string)Application.Current.FindResource("strSqFoot");

          case esriCadastralAreaUnits.esriCAUSquareUSFoot:
            return (string)Application.Current.FindResource("strSqFootUS");
        }

      else

        switch (unit)
        {
          // case esriCAUImperial:res_id = IDS_ACRESROODSPERCHES_SHORT;

          case esriCadastralAreaUnits.esriCAUSquareMeter:
            return (string)Application.Current.FindResource("strMetricShort");

          case esriCadastralAreaUnits.esriCAUMetric:
            return (string)Application.Current.FindResource("strMetricShort");

          //TODO...
          //case esriCAUAcre:res_id = IDS_ACRES_SHORT;
          //case esriCAUHectare:res_id = IDS_HECTARES_SHORT;
          //case esriCAUPerches:res_id = IDS_PERCHES_SHORT;
          //case esriCAUQuarterSections:res_id = IDS_QUARTERSECTIONS_SHORT;
          //case esriCAURoods:res_id = IDS_ROODS_SHORT;
          //case esriCAUSections:res_id = IDS_SECTIONS_SHORT;
          //case esriCAUSquareFoot:res_id = IDS_SQFEET_SHORT;
          //case esriCAUSquareMeter:res_id = IDS_SQMETERS_SHORT;
          //case esriCAUSquareRods:res_id = IDS_SQRODS_SHORT;

          case esriCadastralAreaUnits.esriCAUSquareFoot:
            return (string)Application.Current.FindResource("strSqFootShort");

          case esriCadastralAreaUnits.esriCAUSquareUSFoot:
            return (string)Application.Current.FindResource("strSqFootUSShort");
        }

      return "";
    }


    static public esriCadastralDistanceUnits GetUnit(ref string distance, out bool error)
    {
      string[] units = { "m", "mm", "cm", "km", "ft", "'",  "yd", "in",  "\"", "mi", "ch", "k", 
                        "rd", "ftus", "ydus", "mius", "chus", "kus", "rdus", "rmi", "nm" };

      error = false;
      string unitPart = "";
      string numberPart = "";
      bool started = false;
      foreach (char ch in distance)
      {
        if (!started)
          started = char.IsLetter(ch);
        if (started)
          unitPart += char.ToLower(ch);
        else
          numberPart += ch;
      }
      distance = numberPart;

      if (unitPart == "")
        return esriCadastralDistanceUnits.eUnknown;

      // Compare strings
      Int32 index = 0;
      foreach (string unit in units)
      {
        if (unitPart == unit)
          break;
        index++;
      }

      esriCadastralDistanceUnits du = esriCadastralDistanceUnits.eUnknown; 
      switch (index)
      {
        case 0: du = esriCadastralDistanceUnits.eMeters;
          break;
        case 1: du = esriCadastralDistanceUnits.eMillimeters;
          break;
        case 2: du = esriCadastralDistanceUnits.eCentimeters;
          break;
        case 3: du = esriCadastralDistanceUnits.eKilometers;
          break;
        case 4:
        case 5: du = esriCadastralDistanceUnits.eFeet;
          break;
        case 6: du = esriCadastralDistanceUnits.eYards;
          break;
        case 7:
        case 8: du = esriCadastralDistanceUnits.eInches;
          break;
        case 9: du = esriCadastralDistanceUnits.eMiles;
          break;
        case 10: du = esriCadastralDistanceUnits.eChains;
          break;
        case 11:du = esriCadastralDistanceUnits.eLinks;
          break;
        case 12: du = esriCadastralDistanceUnits.eRods;
          break;
        case 13: du = esriCadastralDistanceUnits.eSurveyFeet;
          break;
        case 14: du = esriCadastralDistanceUnits.eSurveyYards;
          break;
        case 15: du = esriCadastralDistanceUnits.eSurveyMiles;
          break;
        case 16: du = esriCadastralDistanceUnits.eSurveyChains;
          break;
        case 17: du = esriCadastralDistanceUnits.eSurveyLinks;
          break;
        case 18: du = esriCadastralDistanceUnits.eSurveyRods;
          break;
        case 19: du = esriCadastralDistanceUnits.eRomanMiles;
          break;
        case 20: du = esriCadastralDistanceUnits.eNauticalMiles;
          break;
        default:
          error = true;
          break;
      }

      return du;
    }

    static public double UnitFactor(esriCadastralDistanceUnits du)
    {
      switch (du)
      {
        case esriCadastralDistanceUnits.eMeters:
          return 1.0;
        case esriCadastralDistanceUnits.eMillimeters:
          return 0.001;
        case esriCadastralDistanceUnits.eCentimeters:
          return 0.01;
        case esriCadastralDistanceUnits.eKilometers:
          return 1000.0;
        case esriCadastralDistanceUnits.eFeet:
          return 0.3048;
        case esriCadastralDistanceUnits.eYards:
          return 0.9144;
        case esriCadastralDistanceUnits.eInches:
          return 0.0254;
        case esriCadastralDistanceUnits.eMiles:
          return 1609.344;
        case esriCadastralDistanceUnits.eChains:
          return 20.1168;
        case esriCadastralDistanceUnits.eLinks:
          return 0.201168;
        case esriCadastralDistanceUnits.eRods:
          return 5.0292;
        case esriCadastralDistanceUnits.eSurveyFeet:
          return (1200.0 / 3937.0);
        case esriCadastralDistanceUnits.eSurveyYards:
          return (1200.0 / 3937.0) * 3.0;
        case esriCadastralDistanceUnits.eSurveyMiles:
          return (1200.0 / 3937.0 * 5280.0);
        case esriCadastralDistanceUnits.eSurveyChains:
          return (1200.0 / 3937.0 * 66.0);
        case esriCadastralDistanceUnits.eSurveyLinks:
          return (1200.0 / 3937.0 * 0.66);
        case esriCadastralDistanceUnits.eSurveyRods:
          return (1200.0 / 3937.0 * 16.5);
        case esriCadastralDistanceUnits.eRomanMiles:
          return 2375.0 / 1.5;
        case esriCadastralDistanceUnits.eNauticalMiles:
          return 1852.0;
      }

      return 1.0;
    }

  }
}
