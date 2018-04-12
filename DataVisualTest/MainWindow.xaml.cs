using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Windows.Controls.DataVisualization.Charting;
using System.Collections.ObjectModel;
using System.Windows.Threading;

using CINALib;
using FTD2XX_NET;
using System.IO;

namespace DataVisualTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ObservableCollection<KeyValuePair<double, double>> voltage = new ObservableCollection<KeyValuePair<double, double>>();
        ObservableCollection<KeyValuePair<double, double>> current = new ObservableCollection<KeyValuePair<double, double>>();
        ObservableCollection<KeyValuePair<double, double>> power = new ObservableCollection<KeyValuePair<double, double>>();

        DispatcherTimer timer;
        Random random = new Random(DateTime.Now.Millisecond);

        Cina219 cina = new Cina219();
        private FTDI.FT_DEVICE_INFO_NODE[] devList = new FTDI.FT_DEVICE_INFO_NODE[100];

        StreamWriter sw;

        float startPower = 0;
        DateTime startTime = DateTime.MinValue;
        TimeSpan ts;

        Object dataLock = new Object();

        public MainWindow()
        {
            InitializeComponent();
            
            lblStatus.Text = DateTime.Now.ToLongTimeString();

            try
            {
                lineVolts.DataContext = voltage;
                lineCurrent.DataContext = current;
                linePower.DataContext = power;


                cina.UnitCurrent = Cina219.CurrentUnit.mA;
                cina.Init();

                string filename = $"BatteryProfile{DateTime.Now.ToString("MMdd_HHmm")}.csv";
                sw = File.CreateText(filename);
                sw.AutoFlush = true;
                sw.WriteLine($"TimeStamp,Power(mW),Voltage(V),Current(mA)");

                getData();


                timer = new DispatcherTimer();
                timer.Interval = new TimeSpan(0, 0, Int32.Parse(txtInterval.Text));
                timer.Tick += Timer_Tick;

            }
            catch (Exception ex)
            {
                lblStatus.Text = $"{DateTime.Now.ToShortTimeString()} {ex.Message}";
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            getData();
        }

        void getData()
        {
            lock (dataLock)
            {

                bool ovf = false;
                float v = cina.GetVoltage(ref ovf);
                float i = cina.GetCurrent();
                float p = v * i;

                DateTime dateTime = DateTime.Now;

                if (startTime == DateTime.MinValue)
                    startTime = dateTime;

                ts += dateTime - startTime;

                voltage.Add(new KeyValuePair<double, double>(ts.TotalMinutes, v));
                current.Add(new KeyValuePair<double, double>(ts.TotalMinutes, i));
                power.Add(new KeyValuePair<double, double>(ts.TotalMinutes, p));

                // This is so we can calculate a linear power per h rate
                if (startPower == 0 && p > 0)
                {
                    startPower = p;
                    if (startTime == DateTime.MinValue)
                        startTime = dateTime;
                }

                float deltaPower_fromStart = p - startPower; // Should always be negative unless the battery is recharged
                double rate_fromStart = deltaPower_fromStart / ts.TotalHours; // mw/h

                lblMsg.Content = $"{dateTime} Power = {p}mW Voltage = {v}V  Current = {i}mA Rate = {rate_fromStart}mW/h";

                while (true)
                {
                    try
                    {
                        sw.WriteLine($"{dateTime},{p},{v},{i}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        lblStatus.Text = $"{DateTime.Now.ToShortTimeString()} {ex.Message}";
                    }

                }

                //power.Add(new KeyValuePair<DateTime, double>(DateTime.Now, random.NextDouble()));
            }
        }

        private void Button_ReadClick(object sender, RoutedEventArgs e)
        {
            getData();
        }

        private void sliderTimeValuveChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if(timer != null)
                timer.Interval = new TimeSpan(0, 0, Int32.Parse(txtInterval.Text));
        }

        private void Button_StartClick(object sender, RoutedEventArgs e)
        {
            if (timer != null)
                timer.Start();
        }
    }
}
