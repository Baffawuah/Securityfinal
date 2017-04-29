using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using PacketDotNet;
using SharpPcap;
using Excel = Microsoft.Office.Interop.Excel;
using System.IO;
using System.Xml;
using GMap.NET;
using NetFwTypeLib;
using System.Runtime.InteropServices;


namespace MyPacketCapturer
{
    public partial class frmCapture : Form
    {

        frmSend fSend;
        CaptureDeviceList devices;  //List of devices for this computers
        public static ICaptureDevice device;  //the device we will be using
        public static string stringPackets = "";  //data that was captured
        static int numPackets = 0;
        
        public static int network = 0;//counter for traffic
        
        //coordinates used to set the zoom level and to center the map
        static double maxLat = 0;
        static double minLat = 0;
        static double maxLong = 0;
        static double minLong = 0;

        //string to be placed in txtLocations
        private static string counter = "";
        private static List<LocatorCounter> navpoints = new List<LocatorCounter> { };
        private static GMap.NET.WindowsForms.GMapOverlay markers = new GMap.NET.WindowsForms.GMapOverlay("markers");

        public frmCapture()
        {
            InitializeComponent();

            //Bing Map is supposed to be the fastest Map Provider
            gMap.MapProvider = GMap.NET.MapProviders.BingMapProvider.Instance;
          
            GMap.NET.GMaps.Instance.Mode = GMap.NET.AccessMode.ServerOnly;

            //get the list of devices
            devices = CaptureDeviceList.Instance;

            //make sure that there is at least one device
            if (devices.Count < 1)
            {
                MessageBox.Show("no Capture Devices Found!");
                Application.Exit();
            }

            //add devices to the combo box
            foreach (ICaptureDevice dev in devices)
            {
                cmbDevices.Items.Add(dev.Description);
            }

            //get the third device and display in combo box
            device = devices[0];
            cmbDevices.Text = device.Description;

            //register our handler function to the packet arrival event
            device.OnPacketArrival += new SharpPcap.PacketArrivalEventHandler(device_OnPacketArrival);

            int readTimeoutMilliseconds = 1000;
            device.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);
        }

        private static void device_OnPacketArrival(object sender, CaptureEventArgs packet)
        {
            //it skips random packet number
            //increment number of packets captured
            numPackets++;

            //put the packet number in the capture window
            stringPackets += "Packet Number: " + Convert.ToString(numPackets);
            stringPackets += Environment.NewLine;

            //array to store our data
            byte[] data = packet.Packet.Data;

            //keep track of the number of bytes displayed per line
            int byteCounter = 0;
            String senderIPStr = "";
            String senderIPARP = "";
            String senderIPIP = "";
            bool ip = false;
            bool arp = false;
            bool routetable = false;
            stringPackets += "Destination MAC Address: ";
            //parsing the packets 
            foreach (byte b in data)
            {

                //add the byte to our string in hexadecimal
                if (byteCounter <= 13) stringPackets += b.ToString("X2") + " ";
                //gets the IP Address for ARP and IP packets
                if (byteCounter >= 28 && byteCounter <= 31) senderIPARP += b.ToString("X2");
                if (byteCounter >= 26 && byteCounter <= 29) senderIPIP += b.ToString("X2");
                byteCounter++;
                switch (byteCounter)
                {
                    case 6: stringPackets += Environment.NewLine;
                        stringPackets += "Source MAC Address: ";
                        break;
                    case 12: stringPackets += Environment.NewLine;
                        stringPackets += "EtherType: ";
                        break;
                    case 14: if (data[12] == 8)
                        {
                            if (data[13] == 0)
                            {
                                stringPackets += "(IP)";
                                ip = true;
                            }
                            if (data[13] == 6)
                            {
                                stringPackets += "(ARP)";
                                arp = true;
                            }

                        }
                        break;
                    case 27:
                        if (arp || ip)
                        {
                            stringPackets += Environment.NewLine;
                            stringPackets += "Sender IP Address: ";
                        }

                        break;
                }

            }
            if (ip) senderIPStr = senderIPIP;
            if (arp) senderIPStr = senderIPARP;

            //convert to dotted decimal, because the website I'm using needs this format
            String dottedIP = "";
            for (int i = 1; i < senderIPStr.Length; i += 2)
            {
                String hexIP = senderIPStr.Substring(i - 1, 2);
                int decValue = int.Parse(hexIP, System.Globalization.NumberStyles.HexNumber);
                if (i < senderIPStr.Length - 1) dottedIP += decValue + ".";
                else dottedIP += decValue;
            }

            StringBuilder output = new StringBuilder();
            if (arp || ip)
            {
                stringPackets += dottedIP;
                stringPackets += Environment.NewLine;

                int ind = dottedIP.IndexOf('.');
                int ind2 = dottedIP.IndexOf('.', ind + 1);
                if(dottedIP.Substring(0,ind)=="10"||(dottedIP.Substring(0,ind)=="172"&&Convert.ToInt32(dottedIP.Substring(ind+1,ind2-ind-1))>=16&&Convert.ToInt32(dottedIP.Substring(ind+1,ind2-ind-1))<32)
                    || dottedIP.Substring(0, ind2) == "192.168")
                {
                    stringPackets += "Intranetwork traffic";
                    network++;

                }
                else
                {
                    DataTable table = getLocation(dottedIP);
                    foreach (DataRow dataRow in table.Rows)
                    {
                        int r = 0;
                        foreach (DataColumn dataColum in table.Columns)
                        {
                            if (r != 0) output.AppendFormat("{0} ", dataRow[dataColum]);
                            r++;
                        }
                        output.AppendLine();
                    }
                    if (output.ToString().Contains("0 0 0 ")) stringPackets += "Special Address. Not Routetable." + Environment.NewLine;
                    else
                    {
                        stringPackets += output;
                        routetable = true;
                    }
                }
                

            }

            if (routetable)
            {
                LocatorCounter lc = null;
                Boolean first = true;
                foreach (var x in navpoints)
                {
                    if (x.getLocation().Equals(output))
                    {
                        lc = x;
                        first = false;
                        break;
                    }
                }
                if (first)
                {
                    lc = new LocatorCounter(output);
                    navpoints.Add(lc);
                    pinpointLocation(output);
                }
                else lc.increase(); 
                counter = "";
                foreach (var x in navpoints)
                {
                    counter += "The Packet count is : " + x.getCount() + " from Location: " + x.getLocation();
                }

            }


            stringPackets += Environment.NewLine + Environment.NewLine;

            byteCounter = 0;
            stringPackets += "Raw Data" + Environment.NewLine;
            foreach (byte b in data)
            {
                stringPackets += b.ToString("X2") + " ";
                byteCounter++;

                if (byteCounter == 16)
                {
                    byteCounter = 0;
                    stringPackets += Environment.NewLine;
                }

            }
            stringPackets += Environment.NewLine;
            stringPackets += Environment.NewLine;
        }


        private void btnStartStop_Click(object sender, EventArgs e)
        {
            try
            {
                if (btnStartStop.Text == "Start")
                {
                    device.StartCapture();
                    timer1.Enabled = true;
                    btnStartStop.Text = "Stop";
                }
                else
                {   
                    btnStartStop.Text = "Start";
                    device.StopCapture();
                    timer1.Enabled = false;
                }
            }
            catch
            {

            }
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            txtLocations.Text = "Packetcount: " + network + " from private IP's" + Environment.NewLine + counter;
            txtCapturedData.AppendText(stringPackets);
            stringPackets = "";
            txtNumPackets.Text = Convert.ToString(numPackets);

        }

        private void cmbDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            device = devices[cmbDevices.SelectedIndex];
            cmbDevices.Text = device.Description;
            txtGUID.Text = device.Name;
            device.OnPacketArrival += new SharpPcap.PacketArrivalEventHandler(device_OnPacketArrival);

            int readTimeoutMilliseconds = 1000;
            device.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog1.Filter = "Text Files|*.txt|All Files|*.*";
            saveFileDialog1.Title = "Save the Captured Packets";
            saveFileDialog1.ShowDialog();
            if (saveFileDialog1.FileName != "")
            {
                System.IO.File.WriteAllText(saveFileDialog1.FileName, txtCapturedData.Text);
            }
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = "Text Files|*.txt|All Files|*.*";
            openFileDialog1.Title = "open the Captured Packets";
            openFileDialog1.ShowDialog();
            if (openFileDialog1.FileName != "")
            {
                txtCapturedData.Text = System.IO.File.ReadAllText(openFileDialog1.FileName);
            }
        }
        private void sendWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (frmSend.instantiations == 0)
            {
                fSend = new frmSend();
                fSend.Show();
            }
        }

        private static void pinpointLocation(StringBuilder s)
        {
            try
            {
                gMap.Overlays.Clear();
                String[] strings = s.ToString().Split(new char[] { ' ' });
                int length = strings.Length;
                double latitude = Double.Parse(strings[length - 4]);
                double longitude = Double.Parse(strings[length - 3]);

                if (markers.Markers.Count > 0)
                {   
                    if (latitude > maxLat) maxLat = latitude;
                    else if (latitude < minLat) minLat = latitude;
                    if (longitude > maxLong) maxLong = longitude;
                    else if (longitude < minLong) minLong = longitude;
                }
                else
                {   
                    maxLat = latitude+1;
                    maxLong = longitude+1;
                    minLat = latitude-1;
                    minLong = longitude-1;
                }
                
                GMap.NET.WindowsForms.GMapMarker marker =
                    new GMap.NET.WindowsForms.Markers.GMarkerGoogle(
                    new GMap.NET.PointLatLng(latitude, longitude),
                    GMap.NET.WindowsForms.Markers.GMarkerGoogleType.red_small);
                gMap.Overlays.Add(markers);
                marker.ToolTipText = s.ToString();
                markers.Markers.Add(marker);
                double mid1 = (maxLat - minLat)/2+minLat;
                double mid2 = (maxLong - minLong)/2+minLong;
                double dist1 = maxLong - minLong;
                double dist2 = maxLat - minLat;
                
                try
                {gMap.SetPositionByKeywords(mid1 + " " + mid2);}
                catch
                {}
                try
                {gMap.Zoom = 2;}
                catch
                {}
               while(gMap.ViewArea.WidthLng > dist1 && gMap.ViewArea.HeightLat > dist2 && gMap.Zoom <= 15)
                    {increaseZoom();}
               if(gMap.Zoom>2)gMap.Zoom--;  
            }
            catch
            {}
        }
        private static void increaseZoom(){
            try{gMap.Zoom++;}
            catch{}
        }

        private static DataTable getLocation(string varIPAddress)
        {
            WebRequest varWebRequest = WebRequest.Create("http://freegeoip.net/xml/" + varIPAddress);
            WebProxy px = new WebProxy("http://freegeoip.net/xml/" + varIPAddress, true);
            varWebRequest.Proxy = px;
            varWebRequest.Timeout = 2000;
            try
            {
                WebResponse rep = varWebRequest.GetResponse();
                XmlTextReader xtr = new XmlTextReader(rep.GetResponseStream());
                DataSet ds = new DataSet();
                ds.ReadXml(xtr);
                return ds.Tables[0];
            }
            catch { return null; };
        }

        private void txtLocations_TextChanged(object sender, EventArgs e)
        {

        }
    }//end of frmCapture class

    //class to keep track of the nr of packets at each location
    partial class LocatorCounter
        {
            private int count = 0;
            private StringBuilder location;
            public LocatorCounter(StringBuilder location)
            {
                this.location = location;
                this.count++;
            }
            public void increase()
            {
                this.count++;
            }
            public int getCount()
            {
                return this.count;
            }
            public StringBuilder getLocation()
            {
                return this.location;
            }
        }
   }
