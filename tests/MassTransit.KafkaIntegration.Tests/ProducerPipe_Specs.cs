namespace MassTransit.KafkaIntegration.Tests
{
    using System;
    using System.Threading.Tasks;
    using Confluent.Kafka;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using TestFramework;
    using Testing;


    public class ProducerPipe_Specs :
        InMemoryTestFixture
    {
        const string Topic = "producer-pipe";

        [Test]
        public async Task Should_produce()
        {
            await using var provider = new ServiceCollection()
                .ConfigureKafkaTestOptions(options =>
                {
                    options.CreateTopicsIfNotExists = true;
                    options.TopicNames = new[] { Topic };
                })
                .AddMassTransitTestHarness(x =>
                {
                    x.AddTaskCompletionSource<ConsumeContext<KafkaMessage>>();
                    x.AddTaskCompletionSource<SendContext>();

                    x.AddRider(rider =>
                    {
                        rider.AddConsumer<TestKafkaMessageConsumer<KafkaMessage>>();

                        rider.AddProducer<KafkaMessage>(Topic);

                        rider.UsingKafka((context, k) =>
                        {
                            k.TopicEndpoint<KafkaMessage>(Topic, nameof(ProducerPipe_Specs), c =>
                            {
                                c.AutoOffsetReset = AutoOffsetReset.Earliest;

                                c.ConfigureConsumer<TestKafkaMessageConsumer<KafkaMessage>>(context);
                            });

                            k.ConfigureSend(s => s.UseFilter(new SendFilter(context.GetRequiredService<TaskCompletionSource<SendContext>>())));
                        });
                    });
                }).BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            ITopicProducer<KafkaMessage> producer = harness.GetProducer<KafkaMessage>();

            var correlationId = NewId.NextGuid();
            await producer.Produce(new
            {
                CorrelationId = correlationId,
                Text = "text"
            }, harness.CancellationToken);

            var result = await provider.GetTask<SendContext>();

            Assert.IsTrue(result.TryGetPayload<KafkaSendContext>(out _));
            Assert.IsTrue(result.TryGetPayload<KafkaSendContext<KafkaMessage>>(out _));
            Assert.AreEqual(correlationId, result.CorrelationId);
            Assert.That(result.DestinationAddress, Is.EqualTo(new Uri($"loopback://localhost/{KafkaTopicAddress.PathPrefix}/{Topic}")));

            await provider.GetTask<ConsumeContext<KafkaMessage>>();
        }


        class SendFilter :
            IFilter<SendContext>
        {
            readonly TaskCompletionSource<SendContext> _taskCompletionSource;

            public SendFilter(TaskCompletionSource<SendContext> taskCompletionSource)
            {
                _taskCompletionSource = taskCompletionSource;
            }

            public async Task Send(SendContext context, IPipe<SendContext> next)
            {
                _taskCompletionSource.TrySetResult(context);
            }

            public void Probe(ProbeContext context)
            {
            }
        }


        public interface KafkaMessage
        {
            Guid CorrelationId { get; }
            string Text { get; }
        }
    }


    public class ProducerPipe_With_KeyResolver_Specs :
        InMemoryTestFixture
    {
        const string Topic = "producer-key-resolver";

        [Test]
        public async Task Should_produce()
        {
            await using var provider = new ServiceCollection()
                .ConfigureKafkaTestOptions(options =>
                {
                    options.CreateTopicsIfNotExists = true;
                    options.TopicNames = new[] { Topic };
                })
                .AddMassTransitTestHarness(x =>
                {
                    x.AddTaskCompletionSource<ConsumeContext<KafkaMessage>>();
                    x.AddTaskCompletionSource<SendContext>();
                    x.AddRider(rider =>
                    {
                        rider.AddConsumer<TestKafkaMessageConsumer<KafkaMessage>>();

                        rider.AddProducer<Guid, KafkaMessage>(Topic, m => m.Message.Key);

                        rider.UsingKafka((context, k) =>
                        {
                            k.TopicEndpoint<Guid, KafkaMessage>(Topic, nameof(ProducerPipe_With_KeyResolver_Specs), c =>
                            {
                                c.AutoOffsetReset = AutoOffsetReset.Earliest;
                                c.ConfigureConsumer<TestKafkaMessageConsumer<KafkaMessage>>(context);
                            });

                            k.ConfigureSend(s => s.UseFilter(new SendFilter(context.GetRequiredService<TaskCompletionSource<SendContext>>())));
                        });
                    });
                }).BuildServiceProvider(true);

            var harness = provider.GetTestHarness();

            await harness.Start();

            ITopicProducer<KafkaMessage> producer = harness.GetProducer<KafkaMessage>();

            var key = NewId.NextGuid();
            await producer.Produce(new { Key = key }, Pipe.Execute<SendContext>(x => x.CorrelationId = key), harness.CancellationToken);

            var result = await provider.GetTask<SendContext>();

            Assert.IsTrue(result.TryGetPayload(out KafkaSendContext<Guid, KafkaMessage> context));
            Assert.AreEqual(context.Key, key);
            Assert.AreEqual(context.CorrelationId, key);
            Assert.That(result.DestinationAddress, Is.EqualTo(new Uri($"loopback://localhost/{KafkaTopicAddress.PathPrefix}/{Topic}")));

            await provider.GetTask<ConsumeContext<KafkaMessage>>();
        }


        class SendFilter :
            IFilter<SendContext>
        {
            readonly TaskCompletionSource<SendContext> _taskCompletionSource;

            public SendFilter(TaskCompletionSource<SendContext> taskCompletionSource)
            {
                _taskCompletionSource = taskCompletionSource;
            }

            public async Task Send(SendContext context, IPipe<SendContext> next)
            {
                _taskCompletionSource.TrySetResult(context);
            }

            public void Probe(ProbeContext context)
            {
            }
        }


        public interface KafkaMessage
        {
            Guid Key { get; }
        }
    }
}
