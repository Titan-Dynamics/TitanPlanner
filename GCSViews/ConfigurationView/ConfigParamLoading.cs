using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    public partial class ConfigParamLoading : UserControl, IActivate, IDeactivate
    {
        public bool gotAllParams
        {
            get
            {
                if (MainV2.comPort.MAV.param.TotalReceived < MainV2.comPort.MAV.param.TotalReported)
                {
                    return false;
                }

                // a vehicle whose background download hasn't started has no parameters and no count yet
                if (VehicleParamLoader.Instance.IsLoading(MainV2.comPort.MAV))
                    return false;

                return true;
            }
        }

        public ConfigParamLoading()
        {
            InitializeComponent();
        }

        public void Activate()
        {
            timer1.Start();
        }

        public void Deactivate()
        {
            timer1.Stop();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (gotAllParams)
                MainV2.View.Reload();
        }

        private void but_forceparams_Click(object sender, EventArgs e)
        {
            // the background download is doing it: a second download would only share the link with it
            if (VehicleParamLoader.Instance.IsLoading(MainV2.comPort.MAV))
            {
                VehicleParamLoader.Instance.Prioritise(MainV2.comPort, MainV2.comPort.MAV.sysid,
                    MainV2.comPort.MAV.compid);
                return;
            }

            MainV2.comPort.getParamList();
        }
    }
}
