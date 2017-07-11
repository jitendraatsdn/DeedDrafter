using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Tasks;
using System.Windows.Threading;

namespace DeedDrafter
{
  /// <summary>
  // Code behind for the parcel navigation/search control.
  /// </summary>
  /// 
  public class FindResultValue : INotifyPropertyChanged
  {
    private string _Layer;
    private string _Item;
    private string _ItemTooltip;
    public ESRI.ArcGIS.Client.Geometry.Geometry Geometry = null;

    public string Layer
    {
      get { return _Layer; }
      set
      {
        _Layer = value;
        NotifyPropertyChanged("Layer");
      }
    }

    public string Item
    {
      get { return _Item; }
      set
      {
        _Item = value;
        NotifyPropertyChanged("Item");
      }
    }

    public string ItemTooltip
    {
      get { return _ItemTooltip; }
      set
      {
        _ItemTooltip = value;
        NotifyPropertyChanged("ItemTooltip");
      }
    }

    public bool HasTooltip
    {
      get { return _ItemTooltip.Length > 0; }
    }

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

  public class FindResults : ObservableCollection<FindResultValue>
  {
    public FindResults()
      : base()
    {
    }
  }

  public partial class MainWindow : Window
  {
    #region Parcel Find
    bool _foundParcel = false;
    Int32 _queryAttributeComplete = 0;
    Int32 _queryAttributeCount = 0;
    private void SearchItem_PreviewKeyUp(object sender, KeyEventArgs e)
    {
      if (e.Key != Key.Enter)
      {
        if (FindResultControl.Visibility == System.Windows.Visibility.Visible)
        {
          ObservableCollection<FindResultValue> findResults = FindResultControl.ItemsSource as ObservableCollection<FindResultValue>;
          findResults.Clear();
          FindResultControl.Visibility = System.Windows.Visibility.Collapsed;
        }
        return;
      }
      if (SearchItem.Text.Trim().Length == 0)
        return;

      // For each search layer (defined in configuration file), fire off a query to gather results. 
      // The UI will display a spinning arrow until all queries have returned.

      _foundParcel = false;
      _queryAttributeCount = _xmlConfiguation.QueryLayers.Count;
      if (_queryAttributeCount > 0)
      {
        _queryAttributeComplete = 0;
        foreach (LayerDefinition defn in _xmlConfiguation.QueryLayers)
          RunFindParcelQuery(defn);

        Loading.Visibility = System.Windows.Visibility.Visible; // spinning arrow
      }
    }

    private void PDE_Find_Expanded(object sender, RoutedEventArgs e)
    {
      Action action = () =>
      {
        SearchItem.Focus();
      };

      Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action);
    } 

    private void RunFindParcelQuery(LayerDefinition layerDefn)
    {
      QueryTask queryTask  = new QueryTask(layerDefn.Layer());
      if (queryTask == null)
        return;

      queryTask.ExecuteCompleted += QueryLayer_ExecuteCompleted;
      queryTask.Failed += QueryLayer_Failed;
      queryTask.CancelAsync();

      Query query = new ESRI.ArcGIS.Client.Tasks.Query();
      query.OutFields.AddRange(layerDefn.AllFields);
      query.ReturnGeometry = true;
      query.OutSpatialReference = ParcelMap.SpatialReference;

      // if the upper function is null/empty (it defaults to UPPER), then 
      // an exact case search is performed.

      string upper = _xmlConfiguation.UpperFunction;
      string wild = _xmlConfiguation.WildcardCharacter;
      string endFn = "";
      if (upper != "")
      {
        upper += "(";
        endFn = ")";
      }

      // if the wide card function is null/empty (it defaults to %), then 
      // an exact search (case and value) is performed.

      string where = "";
      foreach (string field in layerDefn.SearchFields)
      {
        if (where != "")
          where += " or ";

        if (wild == "")
          where += "(" + field + " = '" + SearchItem.Text + "')";
        else
          where += "(" + upper + field + endFn + " like '" + wild + SearchItem.Text.ToUpper() + wild + "')";
      }
      query.Where = where;

      queryTask.ExecuteAsync(query, layerDefn);
    }

    private void QueryLayer_ExecuteCompleted(object sender, QueryEventArgs args)
    {
      ObservableCollection<FindResultValue> findResults = FindResultControl.ItemsSource as ObservableCollection<FindResultValue>;

      if (!_foundParcel)
        findResults.Clear();

      LayerDefinition layerDefn = (LayerDefinition)args.UserState;

      // display search results for this layers query.

      if (args.FeatureSet.Features.Count > 0)
      {
        // Show search window
        if (!_foundParcel)
          FindResultControl.Visibility = System.Windows.Visibility.Visible;

        foreach (Graphic feature in args.FeatureSet.Features)
        {
          _foundParcel = true;

          string name = "";
          foreach (string fieldName in layerDefn.DisplayFields)
          {
            if (name != "")
              name += ", ";

            // Since accessing the dictionary via the [] operator is case sensitive,
            //   ie, name += (string)feature.Attributes[fieldName];
            // we need to enum thru all the values.
            foreach (var att in feature.Attributes)
              if (att.Key.ToLower() == fieldName.ToLower())
              {
                if (att.Value != null)
                  name += att.Value.ToString();
                break;
              }
          }

          if (!_foundParcel)
            ParcelMap.PanTo(feature.Geometry);     // Pan to the first result
          _foundParcel = true;

          FindResultValue resultValue = new FindResultValue() { Layer = layerDefn.Name, Item = name, ItemTooltip = layerDefn.Tooltip };
          resultValue.Geometry = feature.Geometry; // "Zoom to" geometry
          findResults.Add(resultValue);            // display result values
        }
      }
      else
        System.Console.WriteLine("No features returned from {0}", layerDefn.Name);

      // when we have received the same number of replies as we issued, then hide the spinning arrow
      System.Threading.Interlocked.Increment(ref _queryAttributeComplete);
      if (_queryAttributeCount == _queryAttributeComplete)
      {
        Loading.Visibility = System.Windows.Visibility.Collapsed; // spinning arrow
        CalculateAndAddLineGraphics();
      }
    }

    private void QueryLayer_Failed(object sender, TaskFailedEventArgs args)
    {
      LayerDefinition layerDefn = (LayerDefinition)args.UserState;

      System.Threading.Interlocked.Increment(ref _queryAttributeComplete);
      if (_queryAttributeCount == _queryAttributeComplete)
        Loading.Visibility = System.Windows.Visibility.Collapsed;

      MessageBox.Show(layerDefn.Layer() + "\n\n" + (string)Application.Current.FindResource("strQueryServiceSupport") + "\n\n" + args.Error, (string)Application.Current.FindResource("strQueryServiceFailed"));
    }

    private void FindResultControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      // User selected a result, zoom to feature.

      ObservableCollection<FindResultValue> findResults = FindResultControl.ItemsSource as ObservableCollection<FindResultValue>;

      int index = FindResultControl.SelectedIndex;
      if ((index < 0) || (index >= findResults.Count()))
        return;

      if (findResults[index].Geometry != null)
      {
        if (ParcelMap.SpatialReference.Equals(findResults[index].Geometry.SpatialReference))
        {
          ParcelMap.ZoomTo(findResults[index].Geometry.Extent.Expand(1.5));

          // If we zoom into far, the parcel might fall below the min resolution.
          // ParcelMap_ExtentChanged will correct this.
        }
        else
          MessageBox.Show((string)Application.Current.FindResource("strQueryServiceSRNotEqual"), (string)Application.Current.FindResource("strNavigationFailed"));
      }
    }
    #endregion Parcel Find
  }
}
