using System.Windows.Forms;

namespace ECAUM.DemoApp
{
    public static class MyExtensionMethods
    {
        public static void Invoke(this Control control, MethodInvoker action)
        {
            control.Invoke(action);
        }
        public static void BeginInvoke(this Control control, MethodInvoker action)
        {
            control.BeginInvoke(action);
        }
    }
}
