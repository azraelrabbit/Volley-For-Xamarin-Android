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
using VolleyCSharp.CacheCom;

/*
 * 15.4.15 ��д
 */

namespace VolleyCSharp.CacheCom
{
    /// <summary>
    /// �������Ҫ������ʹ�ø���
    /// </summary>
    public class NoCache : ICache
    {
        public Entry Get(string key)
        {
            return null;
        }

        public void Put(string key, Entry entry) { }

        public void Initialize() { }

        public void Invalidate(string key, bool fullExpire) { }

        public void Remove(string key) { }

        public void Clear() { }
    }
}