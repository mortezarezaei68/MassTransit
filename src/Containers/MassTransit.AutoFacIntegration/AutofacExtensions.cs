﻿// Copyright 2007-2014 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Autofac;
    using Autofac.Core;
    using AutofacIntegration;
    using Internals.Extensions;
    using Saga;
    using Saga.SubscriptionConfigurators;
    using SubscriptionConfigurators;


    public static class AutofacExtensions
    {
        /// <summary>
        /// Load the consumer configuration from the specified Autofac LifetimeScope
        /// </summary>
        /// <param name="configurator"></param>
        /// <param name="scope">The LifetimeScope of the container</param>
        /// <param name="name">The name to use for the scope created for each message</param>
        public static void LoadFrom(this IReceiveEndpointConfigurator configurator, ILifetimeScope scope,
            string name = "message")
        {
            IList<Type> concreteTypes = FindTypes<IConsumer>(scope, r => !r.HasInterface<ISaga>());
            if (concreteTypes.Count > 0)
            {
                foreach (Type concreteType in concreteTypes)
                    ConsumerConfiguratorCache.Configure(concreteType, configurator, scope, name);
            }

            IList<Type> sagaTypes = FindTypes<ISaga>(scope, x => true);
            if (sagaTypes.Count > 0)
            {
                foreach (Type sagaType in sagaTypes)
                    SagaConfiguratorCache.Configure(sagaType, configurator, scope, name);
            }
        }

        /// <summary>
        /// Registers a consumer given the lifetime scope specified
        /// </summary>
        /// <typeparam name="TConsumer">The consumer type</typeparam>
        /// <param name="configurator">The service bus configurator</param>
        /// <param name="scope">The LifetimeScope of the container</param>
        /// <param name="name">The name of the scope created per-message</param>
        /// <returns></returns>
        public static IConsumerConfigurator<TConsumer> Consumer<TConsumer>(this IReceiveEndpointConfigurator configurator,
            ILifetimeScope scope, string name = "message")
            where TConsumer : class, IConsumer
        {
            var consumerFactory = new AutofacConsumerFactory<TConsumer>(scope, name);

            return configurator.Consumer(consumerFactory);
        }

        /// <summary>
        /// Registers a saga using the container that has the repository resolved from the container
        /// </summary>
        /// <typeparam name="TSaga"></typeparam>
        /// <param name="configurator"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        public static ISagaConfigurator<TSaga> Saga<TSaga>(this IReceiveEndpointConfigurator configurator, ILifetimeScope scope,
            string name = "message")
            where TSaga : class, ISaga
        {
            var sagaRepository = scope.Resolve<ISagaRepository<TSaga>>();

            var autofacSagaRepository = new AutofacSagaRepository<TSaga>(sagaRepository, scope, name);

            return configurator.Saga(autofacSagaRepository);
        }

        static IList<Type> FindTypes<T>(ILifetimeScope scope, Func<Type, bool> filter)
        {
            return scope.ComponentRegistry.Registrations
                .SelectMany(r => r.Services.OfType<IServiceWithType>(), (r, s) => new {r, s})
                .Where(rs => rs.s.ServiceType.HasInterface<T>())
                .Select(rs => rs.s.ServiceType)
                .Where(filter)
                .ToList();
        }
    }
}