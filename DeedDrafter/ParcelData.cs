using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using ESRI.ArcGIS.Client.Tasks;
using Utilities;
using System.Reflection;
using System.Windows;

namespace DeedDrafter
{
  #region Datagrid
  /// <summary>
  // This primary MVVM / data context for the parcel entry grid.
  /// </summary>
  /// 
  public enum LineCategory
  {
    Boundary = 0,
    Dependent = 1,
    PreciseConnection = 2,
    Connection = 3,
    Radial = 4,
    Road = 5,
    OriginConnection = 6,
    PartConnection = 7
  }

  public enum DisplayLineCategory
  {
    Boundary = 0,
    OriginConnection = 6
  }

  public enum ColumnIndex
  {
    From = 0,
    Category,
    Bearing,
    Distance,
    Radius,
    Parameter2,
    To
  }

  public class ParcelLineRow : INotifyPropertyChanged
  {
    private bool _init = false;

    static string _precision = "F4";  // Grid precision (XML writes F6)

    Configuration _xmlConfig = null;

    static char _tangentChar = '*';
    public static char TangentChar
    {
      get { return ParcelLineRow._tangentChar; }
    }

    public ParcelLineRow()
    {
      // Init row with Configuration constructor.
      // We need to keep this one public, so the data grid will auto create the row.
      // When the grid does that, we switch in a row using the correct constructor.

      // PropertyChangedEventHandler will assert if _init is false.
    }

    public ParcelLineRow(ref Configuration xmlConfig)
    {
      _init = true;
      if (xmlConfig != null)
      {
        BearingFormat = xmlConfig.EntryFormat;
      }

      _xmlConfig = xmlConfig;
    }

    private string _from;
    private LineCategory _category;
    private string _bearing;
    private string _distance;                    // (string) Distances are held as the user entered them (ie, feet)
    private string _arcLength;
    private string _radius;
    private string _parameter2;
    private string _to;

    private EnumBearingFormat _bearingFormat;

    private bool _tangentCurve = false;
    private bool _minorCurve = true;
    private double? _bearingValue = null;
    private double? _arcLengthValue = null;
    private double? _chordLengthValue = null;    // (double) Distances are held in the units of the data source / SR
    private double? _radiusValue = null;
    private double? _radialBearing1 = null;
    private double? _radialBearing2 = null;
    private Int32? _centerPoint = null;
    private bool _bearingError = false;
    private bool _distanceError = false;
    private bool _radiusError = false;
    private bool _param2Error = false;
    EnumBearingFormat _enteredBearingFormat = EnumBearingFormat.eUnknown;

    public double? RadialBearing1
    {
      get { return _radialBearing1; }
    }

    public double? RadialBearing2
    {
      get { return _radialBearing2; }
    }

    public Int32? CenterPoint
    {
      set { _centerPoint = value; }
      get { return _centerPoint; }
    }

    public void SetCurveAttributes(Int32 centerPoint, double bearing1, double bearing2)
    {
      _centerPoint = centerPoint;
      _radialBearing1 = bearing1;
      _radialBearing2 = bearing2;
    }

    public void ResetCurveAttributes()
    {
      _centerPoint = null;
      _radialBearing1 = null;
      _radialBearing2 = null;
    }

    #region String get/setters for grid
    public string From
    {
      get { return _from; }
      set
      {
        _from = value;
        NotifyPropertyChanged("From");
      }
    }

    public LineCategory Category
    {
      get { return _category; }
      set
      {
        _category = value;
        NotifyPropertyChanged("Category");
      }
    }

    public DisplayLineCategory DisplayCategory
    {
      get { return (DisplayLineCategory)_category; }
      set
      {
        _category = (LineCategory)value;
        NotifyPropertyChanged("Category");
      }
    }

    public bool TangentCurve
    {
      get { return _tangentCurve; }
      set 
      {
        if (_tangentCurve != value)
        {
          _tangentCurve = value;
          NotifyPropertyChanged("Bearing");
        }
      }
    }

    public bool MinorCurve
    {
      get { return _minorCurve; }
      set { _minorCurve = value; }
    }

    public bool IsComplete()
    {
      bool hasBearing = Bearing != null && Bearing != "";
      bool hasDistance = Distance != null && Distance != "";
      bool hasRadius = Radius != null && Radius != "";
      bool hasParam2 = Parameter2 != null && Parameter2 != "";

      return hasBearing && (hasDistance || (hasRadius && hasParam2));
    }

    bool HasChar(ref string value, char ch)
    {
      char tangentChar = char.ToLower(ch);

      if (value == null)
        return false;

      bool hasChar = false;
      string trimmed = value.Trim();
      if (trimmed != null)
      {
        Int32 len = trimmed.Length;
        if (len > 0)
        {
          if (char.ToLower(trimmed[0]) == tangentChar)
          {
            hasChar = true;
            trimmed = trimmed.Substring(1, len - 1);
          }
          else if (char.ToLower(trimmed[len - 1]) == tangentChar)
          {
            hasChar = true;
            trimmed = trimmed.Substring(0, len - 1);
          }
        }
      }

      value = trimmed;
      return hasChar;
    }

    public string Bearing
    {
      get
      {
        if (_tangentCurve && (_bearing != null))
          return _tangentChar + _bearing;
        return _bearing;
      }
      set
      {
        bool error = true;
        string bearing = value;
        bool tangent = HasChar(ref bearing, _tangentChar);
        _bearing = bearing;
        if (bearing != "")
          _bearingValue = ParseBearing(bearing, out _enteredBearingFormat, out error);
        if (tangent && error)
          error = false;
        BearingError = error;
        TangentCurve = tangent; // NotifyProperty will use this value. We need to set this first.
        NotifyPropertyChanged("Bearing");
      }
    }

    bool CalculateArcLength(double arcLength)
    {
      double radius = GetRadius();
      if ((arcLength == 0) || (radius == 0))
        return false;

      double chordLength;
      bool isMinor;
      if (!GeometryUtil.CalculateArcLength(radius, arcLength, out chordLength, out isMinor))
        return false;

      _chordLengthValue = chordLength;
      _arcLengthValue = arcLength;
      _minorCurve = isMinor;

      return true;
    }

    public string Distance
    {
      get 
      {
        if (_arcLengthValue == null) 
          return _distance;

        return _arcLength + " a";
      }
      set
      {
        if (_parameter2 != null && value != null)
        {
          _parameter2 = null;
          NotifyPropertyChanged("Parameter2");
        }

        UpdateDistance(value, true);
      }
    }

    private double DistanceInBaseUnits(string distance, out bool error)
    {
      error = false;
      if ((distance == null) || (distance == ""))
        return 0;

      string distanceUnitless = distance;

      esriCadastralDistanceUnits du = GeometryUtil.GetUnit(ref distanceUnitless, out error);
      double metersFactor = 1.0;
      if (_xmlConfig != null)
        metersFactor = _xmlConfig.EntryUnitsPerMeter;    // Entry units factor to meters?
      if (du != esriCadastralDistanceUnits.eUnknown)
        metersFactor = GeometryUtil.UnitFactor(du);      // Did the user override the entry 
                                                         // units with there own factor?
      double dblValue;
      double.TryParse(distanceUnitless, out dblValue);
      return dblValue * (metersFactor / _xmlConfig.SpatialReferenceUnitsPerMeter); // Distance in SR
    }

    private void UpdateDistance(string value, bool isDistance)
    {
      string distance = value;
      bool isArcLength = HasChar(ref distance, 'a');
      if (!isArcLength)
        isArcLength = HasChar(ref distance, '*');

      bool isNegative = HasChar(ref distance, '-');

      bool error;
      double dblValue = DistanceInBaseUnits(distance, out error);

      if (isArcLength)
      {
        _distance = null;
        _arcLength = distance;
        _parameter2 = null;
        _chordLengthValue = null;
        Parameter2Error = dblValue <= 0.0;
        if (!Parameter2Error)
          _arcLengthValue = dblValue;
        CalculateArcLength(dblValue);
      }
      else
      {
        _arcLength = null;
        _arcLengthValue = null;

        bool propertyError = dblValue <= 0.0 || error;
        if (_radius == null)
        {
          // if we are setting null to a null cell, do nothing. User may have clicked in this cell and lost focus
          if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(_parameter2) && !isDistance)
            return;

          DistanceError = propertyError;
          Parameter2Error = false;

          _distance = distance;
          _parameter2 = null;
          _minorCurve = !isNegative;
          _chordLengthValue = propertyError && string.IsNullOrWhiteSpace(Parameter2) ? null : (double?)dblValue;
        }
        else
        {
          // if we are setting null to a null cell, do nothing. User may have clicked in this cell and lost focus
          if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(_distance) && isDistance)
            return;

          DistanceError = false;
          Parameter2Error = propertyError;

          _distance = null;
          _parameter2 = distance;
          _minorCurve = !isNegative;
          _chordLengthValue = propertyError && string.IsNullOrWhiteSpace(Distance) ? null : (double?)dblValue;
        }
      }

      NotifyDistancePropertyChanged();
    }

    public string Radius
    {
      get { return _radius; }
      set
      {
        if ((_radius == null) && (value != null))
        {
          if (_arcLengthValue == null)
            _parameter2 = _distance;
          else
            _parameter2 = null;
          _distance = null;

          // Transfer error notification from distance => param2
          Parameter2Error = DistanceError;
          DistanceError = false;
        }
        else if ((_radius != null) && string.IsNullOrWhiteSpace(value))
        {
          _distance = _parameter2;
          _parameter2 = null;

          // Transfer error notification from param2 => distance
          DistanceError = Parameter2Error;
          Parameter2Error = false;

          value = null; // get the same behavior for white space as null (ie, pressing delete on field)
        }

        bool error;
        _radius = value;
        _radiusValue = DistanceInBaseUnits(_radius, out error);
        RadiusError = error;

        NotifyPropertyChanged("Radius");

        CalculateArcLength(_arcLengthValue.GetValueOrDefault(0));

        NotifyDistancePropertyChanged();
      }
    }

    public string Parameter2
    {
      get 
      {
        if (!_minorCurve && (_parameter2 != null) && (_parameter2 != ""))
          return "-" + _parameter2;

        return _parameter2; 
      }
      set
      {
        UpdateDistance(value, false);
      }
    }

    private void NotifyDistancePropertyChanged()
    {
      NotifyPropertyChanged("Distance");
      NotifyPropertyChanged("Parameter2");
    }

    public string To
    {
      get { return _to; }
      set
      {
        _to = value;
        NotifyPropertyChanged("To");
      }
    }

    public bool BearingError
    {
      get
      {
        return _bearingError;
      }
      set
      {
        _bearingError = value;
        NotifyPropertyChanged("BearingError");
      }
    }

    public bool DistanceError
    {
      get
      {
        return _distanceError;
      }
      set
      {
        _distanceError = value;
        NotifyPropertyChanged("DistanceError");
      }
    }

    public bool RadiusError
    {
      get
      {
        return _radiusError;
      }
      set
      {
        _radiusError = value;
        NotifyPropertyChanged("RadiusError");
      }
    }

    public bool Parameter2Error
    {
      get
      {
        return _param2Error;
      }
      set
      {
        _param2Error = value;
        NotifyPropertyChanged("Parameter2Error");
      }
    }

    public EnumBearingFormat BearingFormat
    {
      get { return _bearingFormat; }
      set { _bearingFormat = value; }
    }

    public EnumBearingFormat EnteredBearingFormat
    {
      get { return _enteredBearingFormat; }
    }

    #endregion String get/setters for grid

    private double GetDoubleValue(string value)
    {
      double retVal;
      double.TryParse(value, out retVal);
      return retVal;
    }

    private Int32 GetInt32Value(string value)
    {
      Int32 retVal;
      Int32.TryParse(value, out retVal);
      return retVal;
    }

    public Int32  GetFrom()   { return GetInt32Value(From); }
    public double GetRadius() { return _radiusValue.GetValueOrDefault(0); }
    public Int32  GetTo()     { return GetInt32Value(To); }

    public double GetParameter2() 
    {
      if (_chordLengthValue == null)
        return GetDoubleValue(Parameter2);

      return _chordLengthValue.Value;
    }

    public double GetBearing(bool asRadian)
    {
      return asRadian ? Math.PI * _bearingValue.GetValueOrDefault(0) / 180.0 : _bearingValue.GetValueOrDefault(0);
    }

    public void SetBearing(double BearingDegree)
    {
      if (BearingDegree >= 360)
        BearingDegree -= 360;
      if (BearingDegree < 0)
        BearingDegree += 360;

      _bearingValue = BearingDegree;
      _bearing = ParseBearing(BearingDegree, _bearingFormat);
      NotifyPropertyChanged("Bearing");
    }

    public void SetBearing(double BearingDegree, bool tangent)
    {
      _tangentCurve = tangent;
      SetBearing(BearingDegree);
    }

    public double ParseAngleDMS(string bearingString, out bool error)
    {
      error = true;
      bool dmsEntry = true;
      if (bearingString == null)
        return 0.0;

      // Strip unnecessary chars and do basic validation.
      //   can't have more then 4 '.' or '-'
      //   can't have any other chars, etc.

      string bearingTrim = "";
      bool containsNum = false;
      bool containsDec = false;
      bool negative = false;
      Int32 countDash = 0;
      foreach (char ch in bearingString)
      {
        char upperCh = char.ToUpper(ch);
        if (char.IsNumber(upperCh))
        {
          bearingTrim += upperCh;
          containsNum = true;
        }
        else if ((upperCh == '-') && !containsNum && !containsDec)
        {
          negative = true;
          continue;
        }
        else if ((upperCh == '.') || (upperCh == '-'))
        {
          if (!containsNum || containsDec || (countDash > 3))
            return 0;
          bearingTrim += upperCh;

          if (upperCh == '.')
            containsDec = true;
          else
            countDash++;
        }
        else if (upperCh != ' ')
          return 0;
      }

      Int32 strLength = bearingTrim.Length;
      if (strLength == 0)
        return 0;

      bool containsDash = bearingTrim.Contains("-"); // is this a quadrant bearing string.
      bool containsDot = bearingTrim.Contains(".");  // is this a DMS bearing string.

      string[] parts = bearingTrim.Split('-');
      Int32 partCount = parts.Count();
      if (partCount < 1)
        return 0;
      if (containsDash) 
      {
        dmsEntry = true;

        if (partCount < 2)
          return 0;

        // Simplify string from 89-59-59.88 to 89.595988
        string simplifiedString = parts[0];
        if (partCount > 1)
        {
          simplifiedString += ".";
          for (Int32 i = 1; i < partCount; i++)
          {
            string subPart = "";
            foreach (char ch in parts[i])
              if (ch != '.')
                subPart += ch;
            if ((i <= 2) && (subPart.Length < 2))   // min and sec need to be expressed as two digits.
              subPart = subPart.PadLeft(2, '0');    // this fixes a number like 5-47-0-4 to be expressed
            simplifiedString += subPart;            // as 5.4700 (not 5.470)
          }
        }
        bearingTrim = simplifiedString;
      }

      parts = bearingTrim.Split('.');
      partCount = parts.Count();
      if (partCount > 2)
        return 0;

      double ddVal;
      double.TryParse(parts[0], out ddVal);
      if (partCount == 1)        // D
      {
      }
      else if (partCount == 2)
      {
        if (!dmsEntry)           // DD
        {
          double.TryParse(bearingTrim, out ddVal);
        }
        else                     // DMS
        {
          Int32 mins, secs, secsPlus;
          string minStr = parts[1].Substring(0, 2);
          Int32.TryParse(minStr, out mins);
          ddVal += mins / 60.0;

          if (parts[1].Length > 2)
          { 
            string secStr = parts[1].Substring(2, 2);
            if ((secStr != null) && (secStr != ""))
            {
              Int32.TryParse(secStr, out secs);
              ddVal += (secs / 3600.0);

              if (parts[1].Length > 4)
              {
                string secPlusStr = parts[1].Substring(4);
                if ((secPlusStr != null) && (secPlusStr != ""))
                {
                  Int32.TryParse(secPlusStr, out secsPlus);
                  ddVal += (secsPlus / 1000000.0);
                }
              }
            }
          }
        }
      }

      if (negative)
        ddVal = -ddVal;

      error = (ddVal < -180) || (ddVal >= 180);

      return ddVal;
    }

    // We have followed ESRI standard when parsing the string bearing
    //   http://resources.arcgis.com/en/help/main/10.1/index.html#//01m60000003z000000
    //
    public double ParseBearing(string bearingString, out EnumBearingFormat enteredBearingFormat, out bool error)
    {
      error = true;

      bool dmsEntry = BearingFormat == EnumBearingFormat.eDMS;
      enteredBearingFormat = BearingFormat;

      if (bearingString == null)
        return 0.0;

      // Strip unnecessary chars and do basic validation.
      //   NS must be at the beginning
      //   EW must be at the end and had a NS
      //   can't have more then 4 '.' or '-'
      //   can't have any other chars, etc.

      string bearingTrim = "";
      bool containsNS = false;
      bool containsEW = false;
      bool containsNum = false;
      bool containsDec = false;
      Int32 countDash = 0;
      foreach (char ch in bearingString)
      {
        char upperCh = char.ToUpper(ch);
        if (char.IsNumber(upperCh))
        {
          if (containsEW)
            return 0;
          bearingTrim += upperCh;
          containsNum = true;
        }
        else if ((upperCh == '.') || (upperCh == '-'))
        {
          if (!containsNum || containsEW || containsDec || (countDash > 3))
            return 0;
          bearingTrim += upperCh;

          if (upperCh == '.')
            containsDec = true;
          else
            countDash++;
        }
        else if ((upperCh == 'N') || (upperCh == 'S'))
        {
          if (containsEW || containsNS || containsNum)
            return 0;
          bearingTrim += upperCh;
          containsNS = true;
        }
        else if ((upperCh == 'E') || (upperCh == 'W'))
        {
          if (containsEW || !containsNS)
            return 0;
          bearingTrim += upperCh;
          containsEW = true;
        }
        else if (upperCh != ' ')
          return 0;
      }
      if (containsNS && !containsEW)
        return 0;

      Int32 strLength = bearingTrim.Length;
      if (strLength == 0)
        return 0;

      // check for NS/EW (and remove NWEW chars)
      Int32 quadrant = 0;
      if (containsNS) // and containsEW (checked above)
      {
        dmsEntry = true;

        Int32 e = strLength - 1;
        if (bearingTrim[0] == 'N')
        {
          if (bearingTrim[e] == 'W')
            quadrant = 4;
          else // East
            quadrant = 1;
        }
        else // South
        {
          if (bearingTrim[e] == 'W')
            quadrant = 3;
          else // East
            quadrant = 2;
        }
        string withoutNSEW = bearingTrim.Substring(1, strLength - 2);
        bearingTrim = "";

        enteredBearingFormat = EnumBearingFormat.eNSWE;

        // Simplify string from 89-59-59.88 to 89.595988
        bool firstDecFound = false;
        foreach (char ch in withoutNSEW)
        {
          bool seperator = (ch == '.') || (ch == '-');
          if (!firstDecFound && seperator)
          {
            bearingTrim += '.';
            firstDecFound = true;
          }
          else if (!seperator)
            bearingTrim += ch;
        }
      }
      bool containsDash = bearingTrim.Contains("-"); // is this a quadrant bearing string.
      bool containsDot = bearingTrim.Contains(".");  // is this a DMS bearing string.

      string[] parts = bearingTrim.Split('-');
      Int32 partCount = parts.Count();
      if (partCount < 1)
        return 0;
      if (containsDash) // strip out quadrant
      {
        dmsEntry = true;

        if (partCount < 2)
          return 0;
        Int32.TryParse(parts[partCount - 1], out quadrant);
        partCount--;
        if ((quadrant < 1) || (quadrant > 4))
          return 0;

        // Simplify string from 89-59-59.88 to 89.595988
        string simplifiedString = parts[0];
        if (partCount > 1)
        {
          simplifiedString += ".";
          for (Int32 i = 1; i < partCount; i++)
          {
            string subPart = "";
            foreach (char ch in parts[i])
              if (ch != '.')
                subPart += ch;
            if ((i <= 2) && (subPart.Length < 2))   // min and sec need to be expressed as two digits.
              subPart = subPart.PadLeft(2, '0');    // this fixes a number like 5-47-0-4 to be expressed
            simplifiedString += subPart;            // as 5.4700 (not 5.470)
          }
        }
        bearingTrim = simplifiedString;

        enteredBearingFormat = EnumBearingFormat.eQuadrantBearing;
      }

      parts = bearingTrim.Split('.');
      partCount = parts.Count();
      if (partCount > 2)
        return 0;

      double ddVal;
      double.TryParse(parts[0], out ddVal);
      if (partCount == 1)        // D
      {
      }
      else if (partCount == 2)
      {
        if (!dmsEntry)           // DD
        {
          double.TryParse(bearingTrim, out ddVal);
        }
        else                     // DMS
        {
          Int32 mins, secs, secsPlus;
          string minStr = parts[1].Substring(0, 2);
          Int32.TryParse(minStr, out mins);
          ddVal += mins / 60.0;

          string secStr = parts[1].Substring(2, 2);
          if ((secStr != null) && (secStr != ""))
          {
            Int32.TryParse(secStr, out secs);
            ddVal += (secs / 3600.0);

            string secPlusStr = parts[1].Substring(4);
            if ((secPlusStr != null) && (secPlusStr != ""))
            {
              Int32.TryParse(secPlusStr, out secsPlus);
              ddVal += (secsPlus / 1000000.0);
            }
          }
        }
      }

      if (quadrant == 0)
        error = (ddVal < 0) || (ddVal >= 360);
      else
        error = (ddVal < 0) || (ddVal > 90);

      // Now add the quadrant to the value.
      double retVal = ddVal; // this is also the same for quadrant 1
      if (quadrant == 2)
        retVal = 180 - ddVal;
      else if (quadrant == 3)
        retVal = 180 + ddVal;
      else if (quadrant == 4)
        retVal = 360 - ddVal;

      return retVal;
    }

    public string ParseBearing(double bearing, EnumBearingFormat enteredBearingFormat)
    {
      if (double.IsNaN(bearing))
        return "";

      if (enteredBearingFormat == EnumBearingFormat.eUnknown)
      {
        return bearing.ToString(_precision);
      }

      if (enteredBearingFormat == EnumBearingFormat.eDD)
      {
        return bearing.ToString(_precision);
      }

      bool displayAsNSWE = ((enteredBearingFormat == EnumBearingFormat.eNSWE) ||
                            (enteredBearingFormat == EnumBearingFormat.eQuadrantBearing));
      Int32 quadrant = 0;
      if (displayAsNSWE)
      {
        if (bearing < 90)
        {
          quadrant = 1;
        }
        else if (bearing < 180)
        {
          bearing = 180-bearing;
          quadrant =  2;
        }
        else if (bearing < 270)
        {
          bearing -= 180;
          quadrant = 3;
        }
        else if (bearing < 360)
        {
          bearing = 360-bearing;
          quadrant = 4;
        }
      }

      // Convert degree to DMS

      Int32 degree = (Int32)bearing;
      double dMinute = Math.Abs((bearing - degree) * 60.0);  // Sure that the remainder of the digits are +ve
      Int32 minute = (Int32)dMinute;
      double dSecond = (dMinute - minute) * 60.0;
      Int32 second = (Int32)dSecond;
      double dDecSecond = (dSecond - second); 

      // Round seconds/minutes/degree based decimal seconds

      if (dDecSecond > 0.5)
      {
        if (second < 59)
          second++;
        else
        {
          second = 0;
          if (minute < 59)
            minute++;
          else
          {
            minute = 0;
            if (quadrant == 0)
            {
              if (degree < 359)
                degree++;
              else
                degree = 0;
            }
            else
            {
              if (degree < 89)
                degree++;
              else
                degree = 0;
            }
          }
        }
      }

      // since we strip the degree from its other parts, the -ve value is lost when the value is zero.
      string sDeg = (bearing < 0) && (degree == 0) ? "-" + degree.ToString() : degree.ToString();
      string sMin = minute.ToString().PadLeft(2, '0');
      string sSec = second.ToString().PadLeft(2, '0');  // Add sDecSecond is we need more precision

      switch (quadrant)
      {
        case 1: return "N" + sDeg + "." + sMin + sSec + "E";
        case 2: return "S" + sDeg + "." + sMin + sSec + "E";
        case 3: return "S" + sDeg + "." + sMin + sSec + "W";
        case 4: return "N" + sDeg + "." + sMin + sSec + "W";
      }

      return sDeg + "." + sMin + sSec;
    }

    public double GetChordDistance()
    {
      return _chordLengthValue.GetValueOrDefault(0.0);
    }

    public double GetDistance()
    {
      bool error;
      double distance = DistanceInBaseUnits(Distance, out error);
      if (distance == 0.0)
      {
        distance = DistanceInBaseUnits(Parameter2, out error);
        if (distance == 0.0)
          distance = _chordLengthValue.GetValueOrDefault(0);
      }
      return distance;
    }

    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    #endregion

    #region Private Helpers
    private void NotifyPropertyChanged(string propertyName)
    {
      System.Diagnostics.Debug.Assert(_init);  // Init grid row with Configuation constructor
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }
    #endregion
  }

  // Link to basic INotifyPropertyChanged implementation 
  public class ParcelData : INotifyPropertyChanged
  {
    public ParcelData()
      : base()
    {
    }

    private ObservableCollection<ParcelLineRow> _recordInfo = new ObservableCollection<ParcelLineRow>();
    public ObservableCollection<ParcelLineRow> GetRecordInfo()
    {
      return _recordInfo;
    }

    Int32 _selectedRow = 0;
    public Int32 SelectedRow
    {
      get { return _selectedRow; }
      set { 
        _selectedRow = value;
        NotifyPropertyChanged("SelectedLine");
      }
    }

    public bool IsLastRowSelected()
    {
      return _selectedRow == _recordInfo.Count-1;
    }

    public ParcelLineRow SelectedLine
    {
      get {
        if (_selectedRow >= _recordInfo.Count)
        {
          _selectedRow = _recordInfo.Count;
          if (_recordInfo.Count > 0)
          {
            if (!_recordInfo[_recordInfo.Count - 1].IsComplete())
              _selectedRow = _recordInfo.Count - 1;
          }
        }
        if (_selectedRow == _recordInfo.Count)
        {
          ParcelLineRow lineRow = new ParcelLineRow(ref _configuration);
          lineRow.Bearing = "*"; // tangent
          _recordInfo.Add(lineRow);
        }
        return _recordInfo[_selectedRow];
      }
    }

    private Configuration _configuration;
    public Configuration Configuration
    {
      get { return _configuration; }
      set 
      {
        _configuration = value;

        // Force XAML to reread document types that are stored in configuration
        NotifyPropertyChanged("DocumentEntries");

        if ((_distanceUnit == "" || _areaUnit == "") && 
             _configuration.SetupUnits(ref _distanceUnit, ref _areaUnit))
        {
          NotifyPropertyChanged("DistanceUnit");
          NotifyPropertyChanged("AreaUnit");
          NotifyPropertyChanged("RotationUnit");
        }
      }
    }

    public string Version
    {
      get
      {
        Assembly appAssembly = Assembly.GetExecutingAssembly();
        if (appAssembly == null)
          return "";
        Version version = appAssembly.GetName().Version;
        if (version == null)
          return "";
        return (string)Application.Current.FindResource("strVersion") + " " +
               version.Major + "." + version.Minor + "." + version.Build + "." + version.MinorRevision;
      }
    }

    private string _parcelName;
    private string _planName;
    private double _miscloseBearing = 0.0;
    private double _miscloseDistance = 0.0;
    private double _miscloseArea = 0.0;
    private bool _miscloseError = true;
    private string _statedArea = "";
    private bool _compassRuleApplied = false;
    private string _distanceUnit = "";
    private string _areaUnit = "";

    private string _scale = "1.0";
    private string _rotation = "0.0";
    private double _scaleValue = 1.0;
    private double _rotationValue = 0.0;

    public double MiscloseBearing
    {
      get { return _miscloseBearing; }
      set
      {
        _miscloseBearing = value;
        NotifyPropertyChanged("MiscloseBearing");
        NotifyPropertyChanged("FormatedMiscloseBearing");
      }
    }

    public EnumBearingFormat BearingFormat
    {
      get
      {
        if (_recordInfo.Count() == 0)
          return EnumBearingFormat.eUnknown;

        return _recordInfo[0].EnteredBearingFormat;
      }
    }

    public string FormatedMiscloseBearing
    {
      get 
      {
        EnumBearingFormat format = BearingFormat;
        if (BearingFormat == EnumBearingFormat.eUnknown)
          return _miscloseBearing.ToString();

        return _recordInfo[0].ParseBearing(_miscloseBearing, format);
      }
    }

    public double MiscloseDistance
    {
      get { return _miscloseDistance; }
      set
      {
        _miscloseDistance = value;
        NotifyPropertyChanged("MiscloseDistance");
      }
    }

    public double MiscloseArea
    {
      get { return _miscloseArea; }
      set
      {
        _miscloseArea = value;
        NotifyPropertyChanged("MiscloseArea");
      }
    }

    double _ratio = 0;
    public double MiscloseRatio
    {
      get { return _ratio; }
      set
      {
        _ratio = value;
        NotifyPropertyChanged("MiscloseRatio");
        NotifyPropertyChanged("FormatedMiscloseRatio");
        NotifyPropertyChanged("MiscloseRatioLabel");
      }
    }
    const double _lowRatio = 10;
    const double _highRatio = 100000;
    public string FormatedMiscloseRatio
    {
      get
      {
        if (_ratio < _lowRatio) return "";
        return _ratio >= _highRatio ? (string)Application.Current.FindResource("strHigh") : "1:" + _ratio.ToString("F0"); 
      }
    }
    public string MiscloseRatioLabel
    {
      get
      {
        if (_ratio < _lowRatio) return "";
        return _ratio >= _highRatio ? (string)Application.Current.FindResource("strAccuracy") : (string)Application.Current.FindResource("strRatio"); 
      }
    }

    public string ParcelName
    {
      get { return _parcelName == null ? _planName : _parcelName; }
      set
      {
        _parcelName = value;
        NotifyPropertyChanged("ParcelName");
      }
    }

    public string PlanName
    {
      get { return _planName; }
      set
      {
        _planName = value;
        NotifyPropertyChanged("PlanName");
      }
    }

    public string StatedArea
    {
      get { return _statedArea; }
      set
      {
        _statedArea = value.Trim();
        NotifyPropertyChanged("StatedArea");
      }
    }

    public bool MiscloseError
    {
      get
      {
        return _miscloseError;
      }
      set
      {
        if (value == true)
          MiscloseArea = MiscloseDistance = MiscloseBearing = 0.0;
        _miscloseError = value;
        NotifyPropertyChanged("MiscloseError");
      }
    }

    public void SetMiscloseInfo(double bearing, double distance, double area, double ratio)
    {
      if ((bearing == 0) && (distance == 0) && (area == 0))
      {
        _miscloseError = true;
        NotifyPropertyChanged("MiscloseError");
      }
      else
      {
        MiscloseBearing = bearing;
        MiscloseDistance = distance;
        MiscloseArea = area;
        MiscloseRatio = ratio;
        MiscloseError = false;
      }
    }

    public bool CompassRuleApplied
    {
      get { return _compassRuleApplied; }
      set 
      { 
        _compassRuleApplied = value;
        NotifyPropertyChanged("CompassRuleApplied");
      }
    }

    public string DistanceUnit
    {
      get { return _distanceUnit; }
    }

    public string AreaUnit
    {
      get { return _areaUnit; }
    }

    public string Scale
    {
      get { return _scale; }
      set { 
        _scale = value;

        double scale;
        double.TryParse(_scale, out scale);
        _scaleValue = scale > 0.0 ? scale : 1.0;

        NotifyPropertyChanged("Scale");
      }
    }

    public string RotationUnit
    {
      get
      {
        if (_configuration == null)
          return "";
        return (string)System.Windows.Application.Current.FindResource(_configuration.EntryFormat == EnumBearingFormat.eDD ? "strAngleUnitDD" : "strAngleUnitDMS"); 
      }
    }

    public string Rotation
    {
      get { return _rotation; }
      set { 
        if (_configuration.EntryFormat == EnumBearingFormat.eDD)
        {
          double dd;
          double.TryParse(value, out dd);
          _rotationValue = GeometryUtil.DegreeToRadian(-dd);
          _rotation = value;
        }
        else
        {
          if (_recordInfo.Count() > 0)
          {
            bool error;
            double rotDeg = _recordInfo[0].ParseAngleDMS(value, out error);
            if (!error)
            {
              _rotationValue = -GeometryUtil.DegreeToRadian(rotDeg);
              _rotation = _recordInfo[0].ParseBearing(rotDeg, EnumBearingFormat.eDMS); ;
            }
          }
        }

        NotifyPropertyChanged("Rotation");
      }
    }

    public double ScaleValue
    {
      get { return _scaleValue; }
      set
      {
        _scaleValue = value;
        _scale = value.ToString("F3");
        NotifyPropertyChanged("Scale");
      }
    }

    public double RotationValue
    {
      get { return _rotationValue; }
      set
      {
        _rotationValue = value;
        double rotDegree = GeometryUtil.RadianToDegree(-value);
        if (_configuration.EntryFormat == EnumBearingFormat.eDD)
          _rotation = rotDegree.ToString("F3");
        else if (_recordInfo.Count() > 0)
          _rotation = _recordInfo[0].ParseBearing(rotDegree, EnumBearingFormat.eDMS);

        NotifyPropertyChanged("Rotation");
      }
    }

    #region Document entries
    private CollectionView _documentEntries = null;
    private string _documentEntry;

    public CollectionView DocumentEntries
    {
      get {
        if ((_documentEntries == null) && (_configuration != null))
          _documentEntries = new CollectionView(_configuration.DocumentTypes);
        return _documentEntries;
      }
    }

    public string DocumentEntry
    {
      get
      {
        return _documentEntry;
      }
      set
      {
        if (_documentEntry == value) return;
        _documentEntry = value;
        NotifyPropertyChanged("DocumentEntry");
      }
    }
    #endregion

    #region INotifyPropertyChanged Members
    public event PropertyChangedEventHandler PropertyChanged;
    #endregion

    #region Private Helpers
    private void NotifyPropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }
    #endregion
  }
  #endregion

  #region DocumentType
  public class DocumentEntry
  {
    public string Name { get; set; }
    public Int32 Type { get; set; }

    public string SimpleName()
    {
      string returnValue = "";
      foreach (char ch in Name)
        if (ch != ':')
          returnValue += ch;

      return returnValue;
    }

    private string _eaField, _eaValue;
    public bool HasEA
    {
      get { return (_eaField != null) && (_eaField.Length > 0); }
    }
    public string EAValue
    {
      get { return _eaValue; }
    }
    public string EAField
    {
      get { return _eaField; }
    }

    public DocumentEntry(string name, Int32 type, string eaField, string eaValue)
    {
      Name = name;
      Type = type;
      _eaField = eaField;
      _eaValue = eaValue;
    }

    public DocumentEntry(string name, Int32 type)
    {
      Name = name;
      Type = type;
    }
  }
  #endregion
}