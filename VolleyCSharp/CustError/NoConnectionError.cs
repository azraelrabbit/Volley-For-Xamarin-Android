using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

/*
 * �Ѻ�ʵ
 */

namespace VolleyCSharp
{
    public class NoConnectionError : NetworkError
    {
        public NoConnectionError()
            : base() { }

        public NoConnectionError(Exception reason)
            : base(reason) { }
    }
}