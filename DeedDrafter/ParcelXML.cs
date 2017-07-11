using System;
using System.Collections.ObjectModel;
using System.Xml;
using ESRI.ArcGIS.Client.Tasks;
using Utilities;
using PointDictionary = System.Collections.Generic.Dictionary<System.Int32, ESRI.ArcGIS.Client.Geometry.MapPoint>;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Geometry;

namespace DeedDrafter
{
  /// <summary>
  // Saves parcel to CE-XML file
  // This routine is designed to only write out one parcel.
  // The intent is for this XML file to be imported in ArcGIS Parcel Editor.
  /// </summary>
  /// 
  static class ParcelXML
  {
    static string _doubleFormat = "F6";  // XML precision

    static public bool WriteCEXML(ref ParcelData parcelData, string xmlFileName, ref Configuration configuration, ESRI.ArcGIS.Client.Geometry.SpatialReference spatialReference, ref PointDictionary pointDictionary, ref MapPoint projectedStartPoint, double scale, double rotation)
    {
      DocumentEntry documentType = parcelData.DocumentEntries.CurrentItem as DocumentEntry; ;

      XmlWriterSettings settings = new XmlWriterSettings();
      settings.Indent = true;
      settings.IndentChars = "  ";
      settings.NewLineChars = "\r\n";
      settings.NewLineHandling = NewLineHandling.Replace; 

      XmlWriter writer = XmlWriter.Create(xmlFileName, settings);
      if (writer == null)
        return false;

      writer.WriteStartDocument();
      writer.WriteStartElement("geodata", "GeoSurveyPacketData", @"http://www.geodata.com.au/schemas/GeoSurvey/ESRI/1.0/");
      writer.WriteElementString("schemaVersion", "2.0");

      WriteUnits(ref writer, ref spatialReference, ref configuration);
      WriteJobParameters(ref writer, ref configuration);
      WritePlans(ref writer, ref parcelData, ref pointDictionary, ref projectedStartPoint, scale, rotation, ref configuration, ref documentType);
      WritePoints(ref writer);
      WriteControl(ref writer);

      writer.WriteEndElement();
      writer.WriteEndDocument();
      writer.Dispose();             // force write of item.

      return true;
    }

    private static void WriteUnits(ref XmlWriter writer, ref ESRI.ArcGIS.Client.Geometry.SpatialReference spatialReference, ref Configuration configuration)
    {
      writer.WriteStartElement("units");

      writer.WriteElementString("distanceUnits", GeometryUtil.PacketUnitString(configuration.SpatialReferenceUnitsPerMeter));
      writer.WriteElementString("angleUnits", "Degree");
      writer.WriteElementString("directionUnits", "Degree");
      writer.WriteElementString("directionFormat", "north azimuth");

      writer.WriteEndElement();
    }

    private static void WriteJobParameters(ref XmlWriter writer, ref Configuration configuration)
    {
      string wkT;
      if (configuration.OutputSpatialReference != null)   // if created via wkId, we don't write SR
        wkT = configuration.OutputSpatialReference.WKT;
      else
        wkT = configuration.SpatialReferenceWKT;          // if defined via wkId or missing, we don't write SR
      if ((wkT == null) || wkT.Length == 0)
        return;

      writer.WriteStartElement("jobParameters");
      writer.WriteElementString("owner", "DeedDrafter");
      writer.WriteElementString("esriSpatialReference", wkT);
      writer.WriteEndElement();
    }

    private static string XMLDistanceUnits(esriUnits unit)
    {
      // These are all the types. Not all can be mapped to esitUnits.
      //
      // _T("Meter"),_T("Metre"),_T("Foot"),_T("Link"),_T("Foot_US"),
      // _T("Link_US"),_T("Chain"),_T("Chain_US"),_T("Rod"),_T("Rod_US")};

      switch (unit)
      {
        case esriUnits.esriFeet:
          return "Foot_US";
        case esriUnits.esriMeters:
          return "Meter";
        //case esriUnits.esriKilometers:
        //case esriUnits.esriMiles:
        //case esriUnits.esriNauticalMiles:
        //case esriUnits.esriYards:
      }

      return "Meter";
    }

    private static string XMLAreaUnit(esriCadastralAreaUnits areaUnit)
    {
      switch (areaUnit)
      {
        case esriCadastralAreaUnits.esriCAUImperial:
          return "Acres, Roods and Perches";
        case esriCadastralAreaUnits.esriCAUMetric:
          return "Square Meters, Hectare or Kilometers";
        case esriCadastralAreaUnits.esriCAUSquareMeter:
          return "Square Meter";
        case esriCadastralAreaUnits.esriCAUHectare:
          return "Hectare";
        case esriCadastralAreaUnits.esriCAUAcre:
          return "Acre";
        case esriCadastralAreaUnits.esriCAUSquareRods:
          return "SquareRods";
        case esriCadastralAreaUnits.esriCAURoods:
          return "Roods";
        case esriCadastralAreaUnits.esriCAUPerches:
          return "Perches";
        case esriCadastralAreaUnits.esriCAUSquareFoot:
          return "SquareFoot";
        case esriCadastralAreaUnits.esriCAUSquareUSFoot:
          return "SquareUSFoot";
        case esriCadastralAreaUnits.esriCAUQuarterSections:
          return "Quarter Sections";
        case esriCadastralAreaUnits.esriCAUSections:
          return "Sections";
      }

      return "Square Meter";
    }

    private static string XMLDirectionUnit(EnumBearingFormat format)
    {
      // These are all the types. Other are not required.
      //
      // _T("Radian"),_T("Degree"),_T("DMS"),_T("Gradian"),_T("Gon");

      switch (format)
      {
        case EnumBearingFormat.eNSWE:
          return "DMS";
        case EnumBearingFormat.eQuadrantBearing:
          return "DMS";
        case EnumBearingFormat.eDMS:
          return "DMS";
        case EnumBearingFormat.eDD:
          return "Degree";
      }

      return "Degree";
    }

    private static string XMLDirectionType(EnumBearingFormat format)
    {
      // These are all the types. Not all of them are mapped.
      //
      // _T("North Azimuth"),_T("South Azimuth"),_T("Polar"),_T("Quadrant")

      if (format == EnumBearingFormat.eQuadrantBearing)
        return "Quadrant";

      return "North Azimuth";
    }

    private static void WritePlans(ref XmlWriter writer, ref ParcelData parcelData, ref PointDictionary pointDictionary, ref MapPoint projectedStartPoint, double scale, double rotation, ref Configuration configuration, ref DocumentEntry documentType)
    {
      ObservableCollection<ParcelLineRow> parcelRecord = parcelData.GetRecordInfo();

      writer.WriteStartElement("plans");
      writer.WriteStartElement("plan");
      writer.WriteElementString("name", parcelData.PlanName);
      writer.WriteElementString("description", documentType.Name);
      writer.WriteElementString("angleUnits", XMLDirectionUnit(parcelData.Configuration.EntryFormat));
      writer.WriteElementString("distanceUnits", GeometryUtil.PacketUnitString(configuration.EntryUnitsPerMeter));
      writer.WriteElementString("directionFormat", XMLDirectionType(parcelData.Configuration.EntryFormat));
      writer.WriteElementString("areaUnits", XMLAreaUnit(parcelData.Configuration.AreaUnit));
      writer.WriteElementString("lineParameters", "ChordBearingAndChordLengthAndRadius");

      WriteParcel(ref writer, ref parcelData, ref pointDictionary, ref projectedStartPoint, scale, rotation, ref configuration, ref documentType);

      writer.WriteEndElement();
      writer.WriteEndElement();
    }

    private static void WriteParcel(ref XmlWriter writer, ref ParcelData parcelData, ref PointDictionary pointDictionary, ref MapPoint projectedStartPoint, double scale, double rotation, ref Configuration configuration, ref DocumentEntry documentType)
    {
      ObservableCollection<ParcelLineRow> parcelRecord = parcelData.GetRecordInfo();
      if (parcelRecord.Count == 0)
        return;

      if (rotation < 0)
        rotation += 360;

      writer.WriteStartElement("parcels");
      writer.WriteStartElement("parcel");
      writer.WriteElementString("name", parcelData.ParcelName);
      if (scale != 1.0)
        writer.WriteElementString("scale", scale.ToString(_doubleFormat));
      if (rotation != 0.0)
        writer.WriteElementString("rotation", rotation.ToString(_doubleFormat));

      string statedArea = "statedArea";
      if (parcelData.StatedArea != "")
        writer.WriteElementString(statedArea, parcelData.StatedArea);
      else if (parcelData.MiscloseArea > 0)
        writer.WriteElementString(statedArea, parcelData.MiscloseArea.ToString("F2"));
      writer.WriteElementString("joined", "false");
      writer.WriteElementString("parcelNo", "1");
      writer.WriteElementString("type", documentType.Type.ToString());
      if ((parcelData.MiscloseDistance != 0.0) && (parcelData.MiscloseBearing != 0.0))
      {
        writer.WriteElementString("miscloseDistance", parcelData.MiscloseDistance.ToString(_doubleFormat));
        writer.WriteElementString("miscloseBearing", parcelData.MiscloseBearing.ToString(_doubleFormat));
      }

      // Write document type EA object
      if (documentType.HasEA)
      {
        writer.WriteStartElement("extendedAttributes");
        writer.WriteStartElement("extendedAttribute");
        writer.WriteElementString("name", documentType.EAField);
        writer.WriteElementString("value", documentType.EAValue);
        writer.WriteElementString("type", "VT_BSTR");
        writer.WriteEndElement();
        writer.WriteEndElement();
      }

      WriteConstructionData(ref writer, ref parcelData, ref pointDictionary, ref projectedStartPoint, ref configuration, parcelRecord[0]);

      WriteLines(ref writer, ref parcelData, ref documentType, ref configuration);

      writer.WriteEndElement(); // parcel
      writer.WriteEndElement(); // parcels
    }

    private static void WriteLines(ref XmlWriter writer, ref ParcelData parcelData, ref DocumentEntry documentType, ref Configuration configuration)
    {
      writer.WriteStartElement("lines");

      // Write non radial lines
      ObservableCollection<ParcelLineRow> parcelRecord = parcelData.GetRecordInfo();
      Int32 firstId = -1, index = 0, count = 0;
      foreach (ParcelLineRow record in parcelRecord)
        if (!IncompleteLine(record))
          count++;

      foreach (ParcelLineRow record in parcelRecord)
      {
        if (IncompleteLine(record))
          continue;

        index++;
        if ((firstId == -1) && (record.Category == LineCategory.Boundary))
          firstId = record.GetFrom();
        Int32 overrideTo = count == index && count != 1 ? firstId : -1;

        if (record.Category != LineCategory.Radial)
          WriteLine(ref writer, record, ref documentType, overrideTo);
      }

      // Write radial lines
      foreach (ParcelLineRow record in parcelRecord)
      {
        if (record.CenterPoint == null)
          continue;

        ParcelLineRow radialRecord = new ParcelLineRow(ref configuration);

        radialRecord.From = record.From;
        radialRecord.To = record.CenterPoint.ToString();
        radialRecord.Bearing = GeometryUtil.RadianToDegree(record.RadialBearing1.GetValueOrDefault(0)).ToString();
        radialRecord.Distance = record.Radius;
        radialRecord.Category = LineCategory.Radial;
        WriteLine(ref writer, radialRecord, ref documentType, -1);

        radialRecord.From = record.To;
        radialRecord.Bearing = GeometryUtil.RadianToDegree(record.RadialBearing2.GetValueOrDefault(0)).ToString();
        WriteLine(ref writer, radialRecord, ref documentType, -1);
      }

      writer.WriteEndElement(); // lines
    }

    // This routine does not write out points, but it does write the start location

    private static void WriteConstructionData(ref XmlWriter writer, ref ParcelData parcelData, ref PointDictionary pointDictionary, ref MapPoint projectedStartPoint, ref Configuration configuration, ParcelLineRow record)
    {

      Int32 pointId = record.GetFrom();
      if (!pointDictionary.ContainsKey(pointId))
        return;

      // Rather than using the point from the dictionary, the client should pass in a projected point. 
      // ESRI.ArcGIS.Client.Geometry.MapPoint startPoint = pointDictionary[pointId];

      ESRI.ArcGIS.Client.Geometry.MapPoint startPoint = projectedStartPoint;
      if (startPoint == null)
        return;

      writer.WriteStartElement("constructionData");
      writer.WriteStartElement("constructionAdjustment");
      writer.WriteStartElement("startPoint");

      double xM = startPoint.X;
      double yM = startPoint.Y;
      if (configuration.HasSpatialReferenceUnit)
      {
        xM *= configuration.SpatialReferenceUnitsPerMeter;
        yM *= configuration.SpatialReferenceUnitsPerMeter;
      }

       writer.WriteElementString("unjoinedPointNo", pointId.ToString());  
      writer.WriteElementString("x", xM.ToString(_doubleFormat));  
      writer.WriteElementString("y", yM.ToString(_doubleFormat));  

      writer.WriteEndElement(); // startPoint

      if (parcelData.CompassRuleApplied)
        writer.WriteElementString("type", "0");  // compass rule = 0

      writer.WriteEndElement(); // constructionAdjustment
      writer.WriteEndElement(); // constructionData
    }

    private static bool IncompleteLine(ParcelLineRow record)
    {
      return (record.GetFrom() == 0) || (record.GetTo() == 0) || (record.GetDistance() == 0);
    }

    private static void WriteLine(ref XmlWriter writer, ParcelLineRow record, ref DocumentEntry documentType, Int32 to)
    {
      if (IncompleteLine(record))
        return;  // incomplete line

      if (to == -1)
        to = record.GetTo();

      writer.WriteStartElement("line");

      writer.WriteElementString("fromPoint", record.GetFrom().ToString());
      writer.WriteElementString("toPoint", to.ToString());
      writer.WriteElementString("bearing", record.GetBearing(false).ToString(_doubleFormat));
      writer.WriteElementString("distance", record.GetChordDistance().ToString(_doubleFormat));
      writer.WriteElementString("category", record.Category.ToString());
      if (documentType != null)
        writer.WriteElementString("type", documentType.Type.ToString());

      double radius = record.GetRadius();

      if ((radius != 0.0) && (record.CenterPoint != null))
      {
        writer.WriteElementString("radius", radius.ToString(_doubleFormat));
        writer.WriteElementString("centerPoint", record.CenterPoint.ToString()); 
      }

      writer.WriteEndElement();
    }

    private static void WritePoints(ref XmlWriter writer)
    {
      // We don't write this out.

      writer.WriteStartElement("points");
      writer.WriteEndElement();
    }

    private static void WriteControl(ref XmlWriter writer)
    {
      // We don't write this out.

      writer.WriteStartElement("controlPoints");
      writer.WriteEndElement();
    }
  }
}
