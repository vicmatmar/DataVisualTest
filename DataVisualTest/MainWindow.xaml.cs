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
using System.Threading;

namespace DataVisualTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ObservableCollection<KeyValuePair<double, double>> voltage, current, power;

        DispatcherTimer timer;
        Random random = new Random(DateTime.Now.Millisecond);

        Cina219 cina = new Cina219();
        //private FTDI.FT_DEVICE_INFO_NODE[] devList = new FTDI.FT_DEVICE_INFO_NODE[100];

        StreamWriter sw;

        int _duration_sec = 60;
        public int Duration_sec { get => _duration_sec; set => _duration_sec = value; }

        Object dataLock = new Object();


        public MainWindow()
        {
            InitializeComponent();

            lblStatus.Text = DateTime.Now.ToLongTimeString();

            try
            {
                cina.UnitCurrent = Cina219.CurrentUnit.mA;
                cina.Init();
                cina.SetACBusPin(0, false);

                string filename = $"BatteryProfile{DateTime.Now.ToString("MMdd_HHmm")}.csv";
                sw = File.CreateText(filename);
                sw.AutoFlush = true;
                sw.WriteLine($"TimeStamp,Power(mW),Voltage(V),Current(mA)");

                timer = new DispatcherTimer();
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

            TimeSpan ts = DateTime.Now - (DateTime)timer.Tag;
            if (ts.TotalSeconds > Duration_sec)
            {
                timer.Stop();
                cina.SetACBusPin(0, false);
                btnStart.Content = "Start";
                //Thread.Sleep(1000);
            }
        }

        void getData()
        {
            lock (dataLock)
            {
                DateTime dateTime = DateTime.Now;
                DateTime startTime = (DateTime)timer.Tag;
                TimeSpan ts = DateTime.Now - startTime;

                bool ovf = false;
                float v = cina.GetVoltage(ref ovf);
                float i = cina.GetCurrent();
                float p = v * i;


                voltage.Add(new KeyValuePair<double, double>(ts.TotalMinutes, v));
                if(i > 0)
                    current.Add(new KeyValuePair<double, double>(ts.TotalMinutes, i));
                if(p > 0)
                    power.Add(new KeyValuePair<double, double>(ts.TotalMinutes, p));

                double rate_fromStart = 0;
                if (power.Count > 0)
                {
                    double deltaPower_fromStart = p - power[0].Value;
                    rate_fromStart = deltaPower_fromStart / ts.TotalHours; // mw/h
                }

                lblMsg.Content = $"{dateTime} Power = {p}mW Voltage = {v}V  Current = {i}mA Rate = {rate_fromStart}mW/h TS={ts.TotalSeconds}s";

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
            }
        }

        private void Button_StartClick(object sender, RoutedEventArgs e)
        {
            if (btnStart.Content.ToString() == "Start")
            {
                btnStart.Content = "Stop";
                Duration_sec = Convert.ToInt32(txtDuration.Text);

                voltage = new ObservableCollection<KeyValuePair<double, double>>();
                current = new ObservableCollection<KeyValuePair<double, double>>();
                power = new ObservableCollection<KeyValuePair<double, double>>();

                lineVolts.DataContext = voltage;
                lineCurrent.DataContext = current;
                linePower.DataContext = power;


                timer.Interval = new TimeSpan(0, 0, 0, 0, Int32.Parse(txtInterval.Text));

                timer.Tag = DateTime.Now;
                timer.Start();

                getData();
                cina.SetACBusPin(0, true);

            }
            else
            {
                btnStart.Content = "Start";
            }
        }
    }
}
