using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using ESRI.ArcGIS.Client.Tasks;
using Utilities;
using ESRI.ArcGIS.Client.Geometry;

namespace DeedDrafter
{
  /// <summary>
  // These classes read the configuration file to setup deed drafter.
  /// </summary>
  /// 
  public class LayerDefinition
  {
    private LayerDefinition()
    {
    }

    public LayerDefinition(string layerUrl, string layerId, string layerDisplayFields, string layerSearchFields, string layerName, string layerTooltip, string layerType)
    {
      url = layerUrl;
      id = layerId;
      name = layerName;
      tooltip = layerTooltip;

      type = layerType.ToLower();
      if (type == "")
        type = "dynamic";

      bool loadDisplayNamesFromSearch = true;
      Dictionary<string, Int32> fields = new Dictionary<string,int>();
      foreach (string field in layerDisplayFields.Split(','))
      {
        string trimed = field.Trim();
        if (trimed == "")
          continue;

        displayFields.Add(trimed);
        loadDisplayNamesFromSearch = false;
        if (!fields.ContainsKey(trimed))
        {
          allFields.Add(trimed);
          fields.Add(trimed, 0);
        }
      }
      foreach (string field in layerSearchFields.Split(','))
      {
        string trimed = field.Trim();
        if (trimed == "")
          continue;

        if (loadDisplayNamesFromSearch)
          displayFields.Add(trimed);
        if (!fields.ContainsKey(trimed))
        {
          allFields.Add(trimed);
          fields.Add(trimed, 0);
        }
        searchFields.Add(trimed);
      }

//    Lets not do this, since this field can be qualified
//
//    string oidField = "OBJECTID";
//    if (!fields.ContainsKey(oidField))
//      allFields.Add(oidField);
    }

    public string Layer()
    {
      return url + "/" + id;
    }

    private string url;
    public string Url
    {
      get { return url; }
      set { url = value; }
    }

    private string id;
    public string Id
    {
      get { return id; }
      set { id = value; }
    }

    private List<string> searchFields = new List<string>();
    public List<string> SearchFields
    {
      get { return searchFields; }
    }

    private List<string> displayFields = new List<string>();
    public List<string> DisplayFields
    {
      get { return displayFields; }
    }

    private List<string> allFields = new List<string>();
    public List<string> AllFields
    {
      get { return allFields; }
    }

    private string name;
    public string Name
    {
      get { return name; }
    }

    private string tooltip;
    public string Tooltip
    {
      get { return tooltip; }
    }

    private string type;
    public string Type
    {
      get { return type; }  // this is in lower case (dynamic, tiled, image)
    }
  }

  public enum EnumBearingFormat
  {
    eUnknown = 0,
    eNSWE,               // in DMS
    eQuadrantBearing,    // in DMS
    eDMS,
    eDD
  }

  public class Configuration
  {
    static string _root = "deedDrafter//";
    bool init = false;

    #region Accessor functions

    private string _title = "Deed Drafter";
    private Int32 _width = 1000;
    private Int32 _height = 650;
    private string _mailTo = "";
    private Int32 _maxGridHeight = 130;
    private Int32 _spatialReferenceWKID = 0;
    private string _spatialReferenceWKT;
    private Int32 _datumTransformationWKID = 0;
    private string _datumTransformationWKT;
    private bool _datumTransformationForward = false;
    private SpatialReference _outputSpatialReference = null;
    private double _outputUnitsPerMeter = 1;
    private double _xMin = 0, _xMax = 0, _yMin = 0, _yMax = 0;
    private string _geometryServerUrl = "http://utility.arcgisonline.com/ArcGIS/rest/services/Geometry/GeometryServer"; // 10.2
    private double _snapTolerance = 10;
    private double _miscloseRatioSnap = 5000;
    private double _miscloseDistanceSnap = 5;
    private string _upperFunction = "UPPER";
    private string _wildcardCharacter = "%";
    private bool _srMapUnitsSet = false;
    private bool _srOutputUnitsSet = false;
    private double _srMapUnitsPerMeter = 1;
    private bool _entryUnitsSet = false;
    private double _entryUnitsPerMeter = 1;
    private EnumBearingFormat _entryFormat = EnumBearingFormat.eDMS;
    private string _queryLabel;

    private string _identifyURL = "";
    private List<int> _identifyLayers = null;
    private List<string> _identifyLayerNames = null;
    private List<string> _identifyLayerUrl = null;

    private esriCadastralAreaUnits _areaUnit = esriCadastralAreaUnits.esriCAUSquareFoot;

    private double _webMercatorScale = 1.0;
    private bool _useQueryIdentify = false;

    List<LayerDefinition> _queryLayers = new List<LayerDefinition>();
    List<LayerDefinition> _snapLayers = new List<LayerDefinition>();
    List<LayerDefinition> _displayLayers = new List<LayerDefinition>();
    List<DocumentEntry> _documentTypes = null;

    public string Title
    {
      get { return _title; }
    }

    public Int32 Width
    {
      get { return _width; }
    }

    public Int32 Height
    {
      get { return _height; }
    }

    public Int32 MaxGridHeight
    {
      get { return _maxGridHeight; }
    }

    public string MailTo
    {
      get { return _mailTo; }
      set { _mailTo = value; }
    }

    public Int32 SpatialReferenceWKID
    {
      get { return _spatialReferenceWKID; }
    }

    public string SpatialReferenceWKT
    {
      get { return _spatialReferenceWKT; }
    }

    public bool HasSpatialReferenceUnit
    {
      get { return _srMapUnitsSet || _srOutputUnitsSet; }
    }

    public bool HasDatumTransformation
    {
      get { return _datumTransformationWKID > 0 || (_datumTransformationWKT != null && _datumTransformationWKT.Length > 0); }
    }

    public Int32 DatumTransformationWKID
    {
      get { return _datumTransformationWKID; }
      set { _datumTransformationWKID = value; }
    }

    public string DatumTransformationWKT
    {
      get { return _datumTransformationWKT; }
      set { _datumTransformationWKT = value; }
    }

    public bool DatumTransformationForward
    {
      get { return _datumTransformationForward; }
      set { _datumTransformationForward = value; }
    }

    private bool HasOutputSpatialReferenceUnit
    {
      get { return _srOutputUnitsSet; }
    }

    public string MapSpatialReferenceUnits 
    {
      set
      {
        if (!GeometryUtil.UnitFactor(value, out _srMapUnitsPerMeter)) // Searches for both esriEnum and standard keywords
        {                                                          // like meters, foot, etc
          double.TryParse(value, out _srMapUnitsPerMeter);
          if (_srMapUnitsPerMeter <= 0)
            _srMapUnitsPerMeter = 1.0;
        }
        _srMapUnitsSet = true;
      }
    }
    
    public double MapSpatialReferenceUnitsPerMeter
    {
      get { return _srMapUnitsPerMeter; }
    }

    public double SpatialReferenceUnitsPerMeter
    {
      get 
      { 
        if (_srOutputUnitsSet)            // Output units needs to have priority 
          return _outputUnitsPerMeter;    // over the map units (_srMapUnitsSet)
        if (_srMapUnitsSet)               // Otherwise, area cal and XML will be 
          return _srMapUnitsPerMeter;        // written wrong.
        return 1.0; 
      }
    }

    public string OutputSpatialReferenceUnits
    {
      set
      {
        if (!GeometryUtil.UnitFactor(value, out _outputUnitsPerMeter)) // Searches for both esriEnum and standard keywords
        {                                                              // like meters, foot, etc
          double.TryParse(value, out _outputUnitsPerMeter);
          if (_outputUnitsPerMeter <= 0)
            _outputUnitsPerMeter = 1.0;
        }
        _srOutputUnitsSet = true;
      }
    }

    public double OutputSpatialReferenceUnitsPerMeter
    {
      get { return _outputUnitsPerMeter; }
    }

    public bool IsExtentSet()
    {
      return (_xMin != 0) || (_xMax != 0) || (_yMin != 0) || (_yMax != 0);
    }

    public double XMin
    {
      get { return _xMin; }
    }

    public double XMax
    {
      get { return _xMax; }
    }

    public double YMin
    {
      get { return _yMin; }
    }

    public double YMax
    {
      get { return _yMax; }
    }

    public string GeometryServerUrl
    {
      get { return _geometryServerUrl; }
    }

    public double SnapTolerance
    {
      get { return _snapTolerance; }
    }

    public double MiscloseRatioSnap
    {
      get { return _miscloseRatioSnap; }
    }

    public double MiscloseDistanceSnap
    {
      get { return _miscloseDistanceSnap; }
    }

    public List<LayerDefinition> QueryLayers
    {
      get { return _queryLayers; }
    }

    public List<LayerDefinition> SnapLayers
    {
      get { return _snapLayers; }
    }

    public List<LayerDefinition> DisplayLayers
    {
      get { return _displayLayers; }
    }

    public List<DocumentEntry> DocumentTypes
    {
      get { return _documentTypes; }
      set { _documentTypes = value; }
    }

    public string UpperFunction
    {
      get { return _upperFunction; }
    }

    public string WildcardCharacter
    {
      get { return _wildcardCharacter; }
    }

    public EnumBearingFormat EntryFormat
    {
      get { return _entryFormat; }
      set { _entryFormat = value; }
    }

    public void SetEntryAngularFormat(string value)
    {
      string format = value.Trim().ToUpper();
      if (format == "DMS")
        _entryFormat = EnumBearingFormat.eDMS;
      else if (format == "DD")
        _entryFormat = EnumBearingFormat.eDD;
    }

    private string EntryUnits
    {
      set
      {
        if (GeometryUtil.UnitFactor(value, out _entryUnitsPerMeter))
          _entryUnitsSet = true;
        else
        {
          double.TryParse(value, out _entryUnitsPerMeter);
          if (_entryUnitsPerMeter <= 0)
            _entryUnitsPerMeter = 1.0;
        }
      }
    }

    public double EntryUnitsPerMeter
    {
      get 
      {
        if (!_entryUnitsSet)
          return _outputUnitsPerMeter;
        return _entryUnitsPerMeter;
      }
    }

    public esriCadastralAreaUnits AreaUnit
    {
      get { return _areaUnit; }
      set { _areaUnit = value; }
    }


    public SpatialReference OutputSpatialReference
    {
      get 
      { 
        return _outputSpatialReference; 
      }
    }

    public bool SetupUnits(ref string distanceUnit, ref string areaUnit)
    {
      distanceUnit = GeometryUtil.GetUnit(EntryUnitsPerMeter, false);
      AreaUnit = GeometryUtil.DefaultAreaUnit(EntryUnitsPerMeter);
      areaUnit = GeometryUtil.GetArea(AreaUnit, false);
      return true;
    }

    public string IdentifyURL
    {
      get { return _identifyURL; }
    }

    public List<int> IdentifyLayerIDs
    {
      get { return _identifyLayers; }
    }

    public int IdentifyLayerCount
    {
      get { return _identifyLayerUrl == null ? 0 : _identifyLayerUrl.Count; }
    }

    public List<string> IdentifyLayerNames
    {
      get { return _identifyLayerNames; }
    }

    public List<string> IdentifyLayerUrl
    {
      get { return _identifyLayerUrl; }
    }

    public string QueryLabel
    {
      get { return _queryLabel; }
    }

    public double WebMercatorScale
    {
      get { return _webMercatorScale; }
      set { _webMercatorScale = value; }
    }

    public bool UseQueryIdentify
    {
      get { return _useQueryIdentify; }
      set { _useQueryIdentify = value; }
    }

    #endregion

    public Configuration()
    {
    }

    public bool ReadConfiguationFile(string configFile, out string statusMessage)
    {
      if (init)
        throw new Exception("ReadConfiguationFile can only be executed once");
      init = true;

      _title = (string)Application.Current.FindResource("strTitle");

      XmlDocument xmlDocument = new XmlDocument();
      try
      {
        xmlDocument.Load(configFile);
        statusMessage = "";
      }
      catch (Exception e)
      {
        statusMessage = e.Message;
        return false;
      }

      ReadApplication(xmlDocument);
      ReadExtent(xmlDocument);
      ReadSearchLabel(xmlDocument, "application", out _queryLabel);
      ReadParcelEntry(xmlDocument);
      ReadGeometryServer(xmlDocument);
      ReadDatabaseInfo(xmlDocument);
      ReadDocumentTypes(xmlDocument);

      ReadLayerInfo(xmlDocument, "operationalLayers", ref _displayLayers, ref _snapLayers, ref _queryLayers);

      // Draw on bottom of layer stack, so the SR default to this layer.
      bool hasBaseLayer = ReadBaseLayer(xmlDocument, "baseLayer", ref _displayLayers);

      if (hasBaseLayer)
        ReadOutputSpatialReference(xmlDocument, "spatialReference");
      else
        ReadMapSpatialReference(xmlDocument, "spatialReference");

      return true;
    }

    private bool GetValue(XmlAttributeCollection amlAttributes, string nodeName, out string value)
    {
      XmlNode xmlNode = amlAttributes.GetNamedItem(nodeName);
      if (xmlNode == null)
      {
        value = "";
        return false;
      }

      string xmlValue = xmlNode.Value;

      value = xmlValue.Trim();
      return value == "" ? false : true;
    }

    private bool ReadApplication(XmlDocument xmlDocument)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root+"application");
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value;
      if (GetValue(amlAttributes, "title", out value))
        _title = value;
      if (GetValue(amlAttributes, "width", out value))
      {
        Int32 intValue;
        Int32.TryParse(value, out intValue);
        if (intValue > 0)
          _width = intValue;
      }
      if (GetValue(amlAttributes, "height", out value))
      {
        Int32 intValue;
        Int32.TryParse(value, out intValue);
        if (intValue > 0)
          _height = intValue;
      }

      GetValue(amlAttributes, "mailTo", out _mailTo);

      if (GetValue(amlAttributes, "maxGridHeight", out value))
      {
        Int32 intValue;
        Int32.TryParse(value, out intValue);
        if (intValue > 0)
          _maxGridHeight = intValue;
      }

      return true;
    }

    private bool ReadMapSpatialReference(XmlDocument xmlDocument, string xmlTag)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root + xmlTag);
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value, wkt;

      // Client code (MainWindow::ConfigureApplication) will respect wkId over wkT
      if (GetValue(amlAttributes, "wkT", out wkt))
      {
        _spatialReferenceWKT = wkt;
        MapSpatialReferenceUnits = ExtractUnitFromWKT(wkt);
      }

      if (_spatialReferenceWKT == null || _spatialReferenceWKT.Length == 0)
      {
        if (GetValue(amlAttributes, "wkId", out value))
          Int32.TryParse(value, out _spatialReferenceWKID);

        if (!HasSpatialReferenceUnit && (GetValue(amlAttributes, "wkIdUnit", out value)))
          MapSpatialReferenceUnits = value; // this allows a double to be set
      }

      return true;
    }

    private bool ReadOutputSpatialReference(XmlDocument xmlDocument, string xmlTag)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root + xmlTag);
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value, wkt;

      // Client code (MainWindow::ConfigureApplication) will respect wkId over wkT
      if (GetValue(amlAttributes, "wkT", out wkt))
      {
        _outputSpatialReference = new SpatialReference(wkt);
        OutputSpatialReferenceUnits = ExtractUnitFromWKT(wkt);
      }

      if (GetValue(amlAttributes, "wkId", out value) && (_outputSpatialReference == null))
      {
        int wkId;
        if (Int32.TryParse(value, out wkId))
          _outputSpatialReference = new SpatialReference(wkId);

        if (!HasOutputSpatialReferenceUnit && (GetValue(amlAttributes, "wkIdUnit", out value)))
          OutputSpatialReferenceUnits = value;
      }

      // Client code (MainWindow::ConfigureApplication) will respect wkId over wkT
      if (GetValue(amlAttributes, "datumTransformationWkT", out wkt))
        _datumTransformationWKT = wkt;

      if (_datumTransformationWKT == null || _datumTransformationWKT.Length == 0)
      {
        if (GetValue(amlAttributes, "datumTransformationWkId", out value))
          Int32.TryParse(value, out _datumTransformationWKID);
      }

      if (GetValue(amlAttributes, "datumTransformationForward", out value) && (value.ToLower() == "true"))
        _datumTransformationForward = true;

      return true;
    }

    private string ExtractUnitFromWKT(string wkt)
    {
      if ((wkt == null) || (wkt.Length == 0))
        return "";

      string subWkt = "PROJCS[";
      if (!wkt.StartsWith(subWkt))
        return "";

      // if we have vertical SR, we need to skip that part, as we look for the test "UNIT" from the end.
      int braceCount = 1;
      for (int i = 7; (i < wkt.Length) && (braceCount > 0); i++)  // start after PROJCS
      {
        char ch = wkt[i];
        if (ch == '[')
          braceCount++;
        else if (ch == ']')
          braceCount--;
        subWkt += ch;
      }

      string unitTag = "UNIT[";
      int unitPos = subWkt.LastIndexOf(unitTag);
      if (unitPos == -1)
        return "";
      unitPos += unitTag.Length;

      string keyValueStr = "";
      bool endFound = false;
      for (int i = unitPos; i < subWkt.Length; i++)
      {
        if (subWkt[i] == ']')
        {
          endFound = true;
          break;
        }
        keyValueStr += subWkt[i];
      }
      if (!endFound)
        return "";

      string[] keyValue = keyValueStr.Split(',');
      if (keyValue.Count() != 2)
        return "";

      return keyValue[1]; // Return the meters per unit
    }

    private bool ReadParcelEntry(XmlDocument xmlDocument)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root+"parcelEntry");
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value;
      GetValue(amlAttributes, "angular", out value);
      SetEntryAngularFormat(value);

      GetValue(amlAttributes, "distance", out value);
      EntryUnits = value;

      if (GetValue(amlAttributes, "miscloseRatioSnap", out value))
      {
        double valueDbl;
        double.TryParse(value, out valueDbl);
        if (valueDbl > 0)
          _miscloseRatioSnap = valueDbl;
      }
        
      if (GetValue(amlAttributes, "miscloseDistanceSnap", out value))
      {
        double valueDbl;
        double.TryParse(value, out valueDbl);
        if (valueDbl > 0)
          _miscloseDistanceSnap = valueDbl;
      }

      return true;
    }

    private bool ReadExtent(XmlDocument xmlDocument)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root+"extent");
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value;
      if (GetValue(amlAttributes, "xMin", out value))
        double.TryParse(value, out _xMin);
      if (GetValue(amlAttributes, "xMax", out value))
        double.TryParse(value, out _xMax);
      if (GetValue(amlAttributes, "yMin", out value))
        double.TryParse(value, out _yMin);
      if (GetValue(amlAttributes, "yMax", out value))
        double.TryParse(value, out _yMax);

      return true;
    }

    private bool ReadGeometryServer(XmlDocument xmlDocument)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root+"geometryServer");
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value;
      if (GetValue(amlAttributes, "url", out value))
        _geometryServerUrl = value;
      if (GetValue(amlAttributes, "snapTolerance", out value))
      {
        double valueDbl;
        double.TryParse(value, out valueDbl);
        if (valueDbl > 0)
          _snapTolerance = valueDbl;
      }

      return true;
    }

    private bool ReadDatabaseInfo(XmlDocument xmlDocument)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root+"databaseSyntax");
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      GetValue(amlAttributes, "upperFunction", out _upperFunction);
      GetValue(amlAttributes, "wildcardCharacter", out _wildcardCharacter);

      return true;
    }

    private bool ReadDocumentTypes(XmlDocument xmlDocument)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root + "documentTypes");

      _documentTypes = new List<DocumentEntry>();
      if (xmlParentNode != null)
      {
        XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

        XmlNodeList nodeList = xmlParentNode.SelectNodes("document");
        foreach (XmlNode xmlNode in nodeList)
        {
          amlAttributes = xmlNode.Attributes;

          string name, type, field, value;
          GetValue(amlAttributes, "name", out name);
          GetValue(amlAttributes, "type", out type);
          GetValue(amlAttributes, "field", out field);
          GetValue(amlAttributes, "value", out value);

          if ((name == null) || (name == ""))
            continue;

          Int32 typeV = 0;
          Int32.TryParse(type, out typeV);
          if ((typeV == 0) && (type != "0"))
            continue;

          if ((field == "") || (field == null) || (value == "") || (value == null))
            _documentTypes.Add(new DocumentEntry(name, typeV));
          else
            _documentTypes.Add(new DocumentEntry(name, typeV, field, value));
        }
      }

      if (_documentTypes.Count == 0)  // Default if no values are given.
      {
        _documentTypes.Add(new DocumentEntry((string)Application.Current.FindResource("strDeedSplit"), 7));
        _documentTypes.Add(new DocumentEntry((string)Application.Current.FindResource("strDeedMapUpdate"), 7));
        _documentTypes.Add(new DocumentEntry((string)Application.Current.FindResource("strSubBoundary"), 5));
        _documentTypes.Add(new DocumentEntry((string)Application.Current.FindResource("strROWVacation"), 7));
        _documentTypes.Add(new DocumentEntry((string)Application.Current.FindResource("strROWDedication"), 6, "SimConDivType", "Public Right Of Way"));
      }

      return true;
    }

    private void ReadSearchLabel(XmlDocument xmlDocument, string xmlTag, out string value)
    {
      string result = "";

      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root + xmlTag);
      if (xmlParentNode != null)
      {
        XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;
        GetValue(amlAttributes, "searchLabel", out result);
      }

      if (result == "")
        value = (string)Application.Current.FindResource("strQueryLabel");
      else 
        value = result;
    }

    private bool ReadBaseLayer(XmlDocument xmlDocument, string xmlTag, ref List<LayerDefinition> layers)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root + xmlTag);
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string url;
      if (!GetValue(amlAttributes, "url", out url))
        return false;

      // Layer name is never displayed, no name search of display fields, base layers are always tiled.
      LayerDefinition layerDef = new LayerDefinition(url, "0", "", "", "BaseLayer", "", "tiled");
      layers.Insert(0, layerDef);

      return true;
    }

    private bool ReadLayerInfo(XmlDocument xmlDocument, string xmlTag, ref List<LayerDefinition> layers, ref List<LayerDefinition> snapLayers, ref List<LayerDefinition> displayLayers)
    {
      XmlNode xmlParentNode = xmlDocument.SelectSingleNode(_root + xmlTag);
      if (xmlParentNode == null)
        return false;

      XmlAttributeCollection amlAttributes = xmlParentNode.Attributes;

      string value, parentUrl = "";
      if (GetValue(amlAttributes, "url", out value))
        parentUrl = value;

      XmlNodeList nodeList = xmlParentNode.SelectNodes("operationalLayer");
      foreach (XmlNode xmlNode in nodeList)
      {
        string url, displayFields, searchFields, name, id, type, tooltip;

        amlAttributes = xmlNode.Attributes;

        GetValue(amlAttributes, "name", out name);

        GetValue(amlAttributes, "tooltip", out tooltip);

        GetValue(amlAttributes, "searchFields", out searchFields);

        GetValue(amlAttributes, "displayFields", out displayFields);
        if (displayFields == "")
          displayFields = searchFields;

        if (!GetValue(amlAttributes, "id", out id))
        {
          System.Diagnostics.Debug.WriteLine("Missing ID on layer. Layer {0} skipped.", name);
          continue;
        }

        if (!GetValue(amlAttributes, "url", out url))
          url = parentUrl;
        if (url.Length == 0)
        {
          System.Diagnostics.Debug.WriteLine("Missing URL on layer. Layer {0} skipped.", name);
          continue;
        }

        if (!GetValue(amlAttributes, "type", out type))
        {
          if (url.Contains(@"/FeatureServer"))
            type = "feature";
          else if (url.Contains(@"/MapService"))
            type = "dynamic";
          else if (url.Contains(@"/ImageService"))
            type = "image";

          // Since tiled services use a MapService URL, we can't default to this. Tiled services 
          // are generally stated as a base layer (in this app), where tiled is the only option. Thus,
          // define your tile service as a base layer. If it needs to be an operational layer, set its
          // type to "tiled".
        }

        LayerDefinition layerDef = new LayerDefinition(url, id, displayFields, searchFields, name, tooltip, type);

        if (GetValue(amlAttributes, "draw", out value) && (value.ToLower() == "true"))
          layers.Insert(0, layerDef);

        if ((displayFields != "") && (searchFields != ""))
          displayLayers.Insert(0, layerDef);

        if (GetValue(amlAttributes, "snap", out value) && (value.ToLower() == "true"))
          snapLayers.Add(layerDef);

        if (GetValue(amlAttributes, "identify", out value) && (value.ToLower() == "true"))
        {
          if (_identifyURL == "")
            _identifyURL = url;

          if ((name == null) || (name.Length == 0))
            name = Application.Current.FindResource("strLayer") + " " + id;

          // If all the identifies come from one URL, try to use an identify query

          int idValue;
          int.TryParse(id, out idValue);
          if (_identifyLayerUrl == null)
          {
            _identifyLayers = new List<int>();
            _identifyLayerNames = new List<string>();
            _identifyLayerUrl = new List<string>();
          }
          if (_identifyURL == url)
            _identifyLayers.Add(idValue);
          else
            UseQueryIdentify = true;   // Automatically use query since we have different sources.

          _identifyLayerNames.Add(name);

          _identifyLayerUrl.Add(url + "/" + idValue);
        }
      }

      return true;
    }
  }
}
