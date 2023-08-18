using AspectCore.DependencyInjection;
using AspectCore.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Infrastructures.Components.Gui
{
    public class GuiAdapterCreator
    {
        public static TAdapter Create<TAdapter>(params object[] args) where TAdapter : class
        {
            var proxyBuilder = new ProxyGeneratorBuilder();
            proxyBuilder.Configure(t =>
            {

            }).ConfigureService(t =>
            {
                t.AddType<TAdapter>();
            });

            var services = proxyBuilder.Build();

            return services.CreateClassProxy<TAdapter>(args);
        }
    }
}
