﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.OData;
using System.Web.OData.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Library;
using Microsoft.Restier.Core;
using Microsoft.Restier.Core.Model;
using Microsoft.Restier.Providers.EntityFramework;
using Microsoft.Restier.Publishers.OData;
using Microsoft.Restier.Publishers.OData.Model;
using Microsoft.Restier.Security;

namespace Microsoft.OData.Service.Sample.Northwind.Models
{
    [EnableRoleBasedSecurity]
    [Grant(ApiPermissionType.All, On = "Customers")]
    [Grant(ApiPermissionType.All, On = "Products")]
    [Grant(ApiPermissionType.All, On = "CurrentOrders")]
    [Grant(ApiPermissionType.All, On = "ExpensiveProducts")]
    [Grant(ApiPermissionType.All, On = "Orders")]
    [Grant(ApiPermissionType.All, On = "Employees")]
    [Grant(ApiPermissionType.All, On = "Regions")]
    [Grant(ApiPermissionType.Inspect, On = "Suppliers")]
    [Grant(ApiPermissionType.Read, On = "Suppliers")]
    [Grant(ApiPermissionType.All, On = "ResetDataSource")]
    public class NorthwindApi : EntityFrameworkApi<NorthwindContext>
    {
        public new NorthwindContext Context { get { return DbContext; } }

        // Imperative views. Currently CUD operations not supported
        public IQueryable<Product> ExpensiveProducts
        {
            get
            {
                return this.GetQueryableSource<Product>("Products")
                    .Where(c => c.UnitPrice > 50);
            }
        }

        public IQueryable<Order> CurrentOrders
        {
            get
            {
                return this.GetQueryableSource<Order>("Orders")
                    .Where(o => o.ShippedDate == null);
            }
        }

        [Operation(HasSideEffects = true)]
        public void IncreasePrice(Product bindingParameter, int diff)
        {
        }

        [Operation(HasSideEffects = true)]
        public void ResetDataSource()
        {
        }

        [Operation]
        public double MostExpensive(IEnumerable<Product> bindingParameter)
        {
            return 0.0;
        }

        protected override IServiceCollection ConfigureApi(IServiceCollection services)
        {
            Type apiType = this.GetType();
            // Add core and convention's services
            services = services.AddCoreServices(apiType)
                .AddAttributeServices(apiType)
                .AddConventionBasedServices(apiType);

            // Add EF related services
            services.AddEfProviderServices<NorthwindContext>();

            // Add customized services, after EF model builder and before WebApi operation model builder
            // This can be added after base.ConfigureApi,
            // add in middle just used as a sample on how to add service in middle of different RESTier services.
            services.AddService<IModelBuilder, NorthwindModelExtender>();

            // This is used to add the publisher's services
            services.AddODataServices<NorthwindApi>();

            return services;
        }

        // Entity set filter
        protected IQueryable<Customer> OnFilterCustomers(IQueryable<Customer> customers)
        {
            return customers.Where(c => c.CountryRegion == "France");
        }

        // Submit logic
        protected void OnUpdatingProducts(Product product)
        {
            WriteLog(DateTime.Now.ToString() + product.ProductID + " is being updated");
        }

        protected void OnInsertedProducts(Product product)
        {
            WriteLog(DateTime.Now.ToString() + product.ProductID + " has been inserted");
        }

        private void WriteLog(string text)
        {
            // Fake writing log method for submit logic demo
        }

        private class NorthwindModelExtender : IModelBuilder
        {
            public IModelBuilder InnerHandler { get; set; }

            public async Task<IEdmModel> GetModelAsync(ModelContext context, CancellationToken cancellationToken)
            {
                var model = await InnerHandler.GetModelAsync(context, cancellationToken);

                // EF Model builder does not build model any more but just entity set name and entity type map
                if (model == null)
                {
                    var collection = context.EntitySetTypeMapCollection;
                    if (collection == null || collection.Count == 0)
                    {
                        return null;
                    }

                    // Collection is set by EF now, and EF model producer will not build model any more
                    var builder = new ODataConventionModelBuilder();
                    MethodInfo method = typeof (ODataConventionModelBuilder).GetMethod("EntitySet",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                    foreach (var pair in collection)
                    {
                        //Build a method with the specific type argument you're interested in
                        var specifiedMethod = method.MakeGenericMethod(pair.Value);
                        var parameters = new object[]
                        {
                            pair.Key
                        };
                        specifiedMethod.Invoke(builder, parameters);
                    }

                    // Clear the map collection to make RESTier model builder will not build the model again.
                    context.EntitySetTypeMapCollection.Clear();
                    model = builder.GetEdmModel();
                }

                // Way 2: enable auto-expand through model annotation.
                var orderType = (EdmEntityType)model.SchemaElements.Single(e => e.Name == "Order");
                var orderDetailsProperty = (EdmNavigationProperty)orderType.DeclaredProperties
                    .Single(prop => prop.Name == "Order_Details");
                model.SetAnnotationValue(orderDetailsProperty,
                    new QueryableRestrictionsAnnotation(new QueryableRestrictions { AutoExpand = true }));

                return model;
            }
        }
    }
}