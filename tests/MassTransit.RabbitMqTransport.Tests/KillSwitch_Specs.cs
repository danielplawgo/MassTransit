namespace MassTransit.RabbitMqTransport.Tests
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using MassTransit.Testing;
    using Monitoring.Health;
    using NUnit.Framework;
    using TestFramework;


    [Category("Flakey")]
    [TestFixture]
    public class KillSwitch_Specs :
        RabbitMqTestFixture
    {
        [Test]
        public async Task Should_be_degraded_after_too_many_exceptions()
        {
            Assert.That(await _busHealth.WaitForHealthStatus(BusHealthStatus.Healthy, TimeSpan.FromSeconds(10)), Is.EqualTo(BusHealthStatus.Healthy));

            await Task.WhenAll(Enumerable.Range(0, 11).Select(x => Bus.Publish(new BadMessage())));

            Assert.That(await _busHealth.WaitForHealthStatus(BusHealthStatus.Degraded, TimeSpan.FromSeconds(15)), Is.EqualTo(BusHealthStatus.Degraded));

            Assert.That(await _busHealth.WaitForHealthStatus(BusHealthStatus.Healthy, TimeSpan.FromSeconds(10)), Is.EqualTo(BusHealthStatus.Healthy));

            await Task.WhenAll(Enumerable.Range(0, 20).Select(x => Bus.Publish(new GoodMessage())));

            Assert.That(await RabbitMqTestHarness.Consumed.SelectAsync<BadMessage>().Take(11).Count(), Is.EqualTo(11));

            Assert.That(await RabbitMqTestHarness.Consumed.SelectAsync<GoodMessage>().Take(20).Count(), Is.EqualTo(20));
        }

        BusHealth _busHealth;

        protected override void ConfigureRabbitMqBus(IRabbitMqBusFactoryConfigurator configurator)
        {
            _busHealth = new BusHealth();

            configurator.UseKillSwitch(options => options
                .SetActivationThreshold(10)
                .SetTripThreshold(0.1)
                .SetRestartTimeout(s: 1));

            configurator.ConnectBusObserver(_busHealth);
            configurator.ConnectEndpointConfigurationObserver(_busHealth);
        }

        protected override void ConfigureRabbitMqReceiveEndpoint(IRabbitMqReceiveEndpointConfigurator configurator)
        {
            configurator.PurgeOnStartup = false;
            configurator.PrefetchCount = 1;

            configurator.Consumer<BadConsumer>();
        }

        public KillSwitch_Specs()
        {
            TestTimeout = TimeSpan.FromMinutes(1);
            TestInactivityTimeout = TimeSpan.FromSeconds(10);
        }


        class BadConsumer :
            IConsumer<BadMessage>,
            IConsumer<GoodMessage>
        {
            public Task Consume(ConsumeContext<BadMessage> context)
            {
                throw new IntentionalTestException("Trying to trigger the kill switch");
            }

            public Task Consume(ConsumeContext<GoodMessage> context)
            {
                return Task.CompletedTask;
            }
        }


        class GoodMessage
        {
        }


        class BadMessage
        {
        }
    }
}
