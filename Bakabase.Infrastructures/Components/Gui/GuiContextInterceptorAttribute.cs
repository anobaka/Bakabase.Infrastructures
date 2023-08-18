using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AspectCore.DynamicProxy;
using Bootstrap.Extensions;

namespace Bakabase.Infrastructures.Components.Gui
{
    public class GuiContextInterceptorAttribute : AbstractInterceptorAttribute
    {
        public override Task Invoke(AspectContext context, AspectDelegate next)
        {
            if (context.Proxy is not GuiAdapter guiAdapter)
            {
                throw new Exception(
                    $"{context.Proxy.GetType().FullName} is not implementation of {nameof(GuiAdapter)}");
            }

            if (context.ServiceMethod.ReturnType == typeof(void))
            {
                guiAdapter.InvokeInGuiContext(() => { context.Invoke(next); });
            }
            else
            {
                guiAdapter.InvokeInGuiContext(() =>
                {
                    context.Invoke(next);
                    return context.ReturnValue;
                });
            }

            return Task.CompletedTask;
        }
    }
}