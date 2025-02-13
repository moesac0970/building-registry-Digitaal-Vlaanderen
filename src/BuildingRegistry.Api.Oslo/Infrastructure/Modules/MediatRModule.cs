namespace BuildingRegistry.Api.Oslo.Infrastructure.Modules
{
    using System.Reflection;
    using Autofac;
    using BuildingRegistry.Api.Oslo.Handlers.Building;
    using Handlers;
    using MediatR;
    using Module = Autofac.Module;

    public class MediatRModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder
                .RegisterType<Mediator>()
                .As<IMediator>()
                .InstancePerLifetimeScope();

            // request & notification handlers
            builder.Register<ServiceFactory>(context =>
            {
                var ctx = context.Resolve<IComponentContext>();
                return type => ctx.Resolve(type);
            });

            builder.RegisterAssemblyTypes(typeof(GetHandler).GetTypeInfo().Assembly).AsImplementedInterfaces();
        }
    }
}
