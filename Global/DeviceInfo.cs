using eMRBCS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Global
{
    internal class MyDeviceInfo
    {
        public static double device_height { get; set; }
        public static double device_width { get; set; }
        public static double device_density { get; set; }

        public static double device_heightpix { get; set; }
        public static double device_widthpix { get; set; }

        public static string device_AndroidID { get; set; }

        public static string activation_file { get; set; } = "eMRBCS.key";

        public static bool isActivated { get; set; }

        public static Printerconfig currentPrintConfig { get; set; }

        public static void ScreenAdjustment()
        {

        }

        private void Screen()
        {

        }
    }
}
