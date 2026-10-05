using System.Diagnostics.Metrics;

namespace IronForge.Application.Observablity.Metrics
{
    public class IronForgeBusinessMetrics
    {
        public static readonly Counter<long> ProductsCreated =
       IronForgeMeter.Meter.CreateCounter<long>(
           "ironforge.business.products.created",
           description: "Number of products successfully created.");

        public static readonly Counter<long> ProductsUpdated =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.products.updated",
                description: "Number of products successfully updated.");

        public static readonly Counter<long> ProductsDeleted =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.business.products.deleted",
                description: "Number of products successfully deleted.");
    }
}
