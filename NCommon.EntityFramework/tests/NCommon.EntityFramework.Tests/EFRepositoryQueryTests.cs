using System;
using System.Data.Objects;
using System.Linq;
using NCommon.Data.EntityFramework.Tests.HRDomain;
using NCommon.Data.EntityFramework.Tests.OrdersDomain;
using NCommon.Extensions;
using NCommon.Specifications;
using NUnit.Framework;
using Rhino.Mocks;

namespace NCommon.Data.EntityFramework.Tests
{
    [TestFixture]
    public class EFRepositoryQueryTests : EFRepositoryTestBase
    {
        [Test]
        public void can_perform_simple_query()
        {
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Customer customer = null;
                testData.Batch(x => customer = x.CreateCustomer());

                using (var scope = new UnitOfWorkScope())
                {
                    var savedCustomer = new EFRepository<Customer>()
                        .Where(x => x.CustomerID == customer.CustomerID)
                        .First();

                    Assert.That(savedCustomer, Is.Not.Null);
                    Assert.That(savedCustomer.CustomerID, Is.EqualTo(customer.CustomerID));
                    scope.Commit();
                }
            }
        }

        [Test]
        public void can_save()
        {
            var customer = new Customer
            {
                FirstName = "Jane",
                LastName = "Doe",
                StreetAddress1 = "123 Main St",
                City = "Sunset City",
                State = "LA",
                ZipCode = "12345"
            };

            using (var scope = new UnitOfWorkScope())
            {
                var repository = new EFRepository<Customer>();
                repository.Save(customer);
                scope.Commit();
            }
            Assert.That(customer.CustomerID, Is.GreaterThan(0));
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Customer savedCustomer = null;
                testData.Batch(action => savedCustomer = action.GetCustomerById(customer.CustomerID));
                Assert.That(savedCustomer, Is.Not.Null);
                Assert.That(savedCustomer.CustomerID, Is.EqualTo(customer.CustomerID));
            }
        }

        [Test]
        public void can_modify()
        {
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Customer customer = null;
                testData.Batch(x => customer = x.CreateCustomer());

                using (var scope = new UnitOfWorkScope())
                {
                    var savedCustomer = new EFRepository<Customer>()
                        .Where(x => x.CustomerID == customer.CustomerID)
                        .First();

                    savedCustomer.LastName = "Changed";
                    scope.Commit();
                }

                testData.Context<OrderEntities>().Refresh(RefreshMode.StoreWins, customer);
                Assert.That(customer.LastName, Is.EqualTo("Changed"));
            }
        }

        [Test]
        public void can_delete()
        {
            var customer = new Customer
            {
                FirstName = "John",
                LastName = "Doe",
            };
            using (var scope = new UnitOfWorkScope())
            {
                new EFRepository<Customer>().Save(customer);
                scope.Commit();
            }
            Assert.That(customer.CustomerID, Is.GreaterThan(0));
            using (var scope = new UnitOfWorkScope())
            {
                var repository = new EFRepository<Customer>();
                var savedCustomer = repository.Where(x => x.CustomerID == customer.CustomerID).First();
                repository.Delete(savedCustomer);
                scope.Commit();
            }

            //Making sure customer is deleted
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Customer savedCustomer = null;
                testData.Batch(x => savedCustomer = x.GetCustomerById(customer.CustomerID));
                Assert.That(savedCustomer, Is.Null);
            }
        }

        [Test]
        public void can_attach()
        {
            var customer = new Customer
            {
                FirstName = "Jane",
                LastName = "Doe"
            };

            var context = OrdersContextProvider() as OrderEntities;
            context.AddToCustomers(customer);
#if NETv40
            context.SaveChanges(SaveOptions.AcceptAllChangesAfterSave);
#else
            context.SaveChanges(true);
#endif
            context.Detach(customer);
            context.Dispose(); //Auto detach

            using (var scope = new UnitOfWorkScope())
            {
                var repository = new EFRepository<Customer>();
                repository.Attach(customer);
                customer.LastName = "Changed";
                scope.Commit(); //Should change since the customer was attached to repository.
            }

            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Customer savedCustomer = null;
                testData.Batch(x => savedCustomer = x.GetCustomerById(customer.CustomerID));
                Assert.That(savedCustomer, Is.Not.Null);
                Assert.That(savedCustomer.LastName, Is.EqualTo("Changed"));
            }
        }

        [Test]
        public void can_query_using_specification()
        {
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                testData.Batch(actions =>
                {
                    actions.CreateOrdersForCustomers(actions.CreateCustomersInState("PA", 2));
                    actions.CreateOrdersForCustomers(actions.CreateCustomersInState("DE", 5));
                    actions.CreateOrdersForCustomers(actions.CreateCustomersInState("LA", 3));
                });

                using (new UnitOfWorkScope())
                {


                    var customersInPA = new Specification<Order>(x => x.Customer.State == "DE");

                    var ordersRepository = new EFRepository<Order>();
                    var results = from order in ordersRepository.Query(customersInPA) select order;

                    Assert.That(results.Count(), Is.GreaterThan(0));
                    Assert.That(results.Count(), Is.EqualTo(5));
                }
            }
        }

        [Test]
        public void can_lazyload()
        {
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Customer customer = null;
                testData.Batch(x =>
                {
                    customer = x.CreateCustomer();
                    x.CreateOrderForCustomer(customer);
                });

                using (var scope = new UnitOfWorkScope())
                {
                    var savedCustomer = new EFRepository<Customer>()
                        .Where(x => x.CustomerID == customer.CustomerID)
                        .First();

                    Assert.That(savedCustomer, Is.Not.Null);
                    Assert.That(savedCustomer.Orders, Is.Not.Null);
                    Assert.DoesNotThrow(savedCustomer.Orders.Load);
                    Assert.That(savedCustomer.Orders.Count, Is.GreaterThan(0));
                    scope.Commit();
                }
            }
        }

        [Test]
        public void lazyloading_when_outside_scope_throws()
        {
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Order order = null;
                testData.Batch(x => order = x.CreateOrderForCustomer(x.CreateCustomer()));

                Order savedOrder = null;
                using (var scope = new UnitOfWorkScope())
                {
                    savedOrder = new EFRepository<Order>()
                        .Where(x => x.OrderID == order.OrderID)
                        .First();
                    scope.Commit();
                }
                Assert.That(savedOrder, Is.Not.Null);
                Assert.That(savedOrder.Customer, Is.Null);
            }
        }

        [Test]
        public void can_eager_fetch_using_Eagerly()
        {
            using (var tesData = new EFTestData(OrdersContextProvider()))
            {
                Customer customer = null;
                tesData.Batch(x =>
                {
                    var products = x.CreateProducts(10);
                    var order = x.CreateOrderForProducts(products);
                    customer = order.Customer = x.CreateCustomer();
                });

                Customer savedCustomer;
                using (var scope = new UnitOfWorkScope())
                {
                    savedCustomer = new EFRepository<Customer>()
                        .Eagerly(f => f.Fetch<Order>(x => x.Orders)
                                        .And<OrderItem>(x => x.OrderItems)
                                        .And<Product>(x => x.Product))
                        .Where(x => x.CustomerID == customer.CustomerID)
                        .First();
                    scope.Commit();
                }

                Assert.That(savedCustomer, Is.Not.Null);
                Assert.That(savedCustomer.Orders, Is.Not.Null);
                Assert.That(savedCustomer.Orders.Count(), Is.EqualTo(1));
                savedCustomer.Orders.ForEach(order =>
                {
                    Assert.That(order.OrderItems, Is.Not.Null);
                    Assert.That(order.OrderItems.Count(), Is.GreaterThan(0));
                    order.OrderItems.ForEach(item => Assert.That(item.Product, Is.Not.Null));
                });
            }
        }

        [Test]
        public void can_eager_fetch_using_with()    
        {
            using (var tesData = new EFTestData(OrdersContextProvider()))
            {
                Order order = null;
                tesData.Batch(x => order = x.CreateOrderForCustomer(x.CreateCustomer()));

                Order savedOrder = null;
                using (var scope = new UnitOfWorkScope())
                {
                    savedOrder = new EFRepository<Order>()
                        .With(x => x.Customer)
                        .Where(x => x.OrderID == order.OrderID)
                        .First();
                    scope.Commit();
                }

                Assert.That(savedOrder, Is.Not.Null);
                Assert.DoesNotThrow(() => { var firstName = savedOrder.Customer.FirstName; });
            }
        }

        public class FakeFetchingStrategy : IFetchingStrategy<Customer, EFRepositoryQueryTests>
        {
            public void Define(IRepository<Customer> repository)
            {
                repository.Eagerly(f => f.Fetch<Order>(x => x.Orders)
                                         .And<OrderItem>(x => x.OrderItems));
            }
        }

        [Test]
        public void can_eager_fetch_using_fetching_strategy()
        {
            using (var testData = new EFTestData(OrdersContextProvider()))
            {
                Locator.Stub(x => x
                    .GetAllInstances<IFetchingStrategy<Customer, EFRepositoryQueryTests>>())
                    .Return(new[] { new FakeFetchingStrategy() });

                Customer customer = null;
                testData.Batch(x =>
                {
                    var products = x.CreateProducts(3);
                    var order = x.CreateOrderForProducts(products);
                    customer = x.CreateCustomer();
                    order.Customer = customer;
                });

                Customer savedCustomer = null;
                using (var scope = new UnitOfWorkScope())
                {
                    savedCustomer = new EFRepository<Customer>()
                        .For<EFRepositoryQueryTests>()
                        .Where(x => x.CustomerID == customer.CustomerID)
                        .First();
                    scope.Commit();
                }

                Assert.That(savedCustomer, Is.Not.Null);
                savedCustomer.Orders.ForEach(order =>
                {
                    Assert.That(order.Customer, Is.Not.Null);
                    Assert.That(order.OrderItems.Count, Is.GreaterThan(0));
                });
            }
        }

        [Test]
        public void can_query_multiple_databases()
        {
            using (var ordersTestData = new EFTestData(OrdersContextProvider()))
            using (var hrTestData = new EFTestData(HRContextProvider()))
            {
                Customer customer = null;
                SalesPerson salesPerson = null;
                ordersTestData.Batch(x => customer = x.CreateCustomer());
                hrTestData.Batch(x => salesPerson = x.CreateSalesPerson());

                //NOTE: This will enlist a Distributed DTC tx.
                using (var scope = new UnitOfWorkScope())
                {
                    var savedCustomer = new EFRepository<Customer>()
                        .Where(x => x.CustomerID == customer.CustomerID)
                        .First();

                    var savedPerson = new EFRepository<SalesPerson>()
                        .Where(x => x.Id == salesPerson.Id)
                        .First();

                    Assert.That(savedCustomer, Is.Not.Null);
                    Assert.That(savedPerson, Is.Not.Null);
                    scope.Commit();
                }
            }
        }
    }
}