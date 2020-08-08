using Prism.Navigation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Prism.Common;
using Prism.Ioc;
using Prism.Behaviors;
using System.Linq;

namespace Prism.Navigation
{
    /// <summary>
    /// Prism's <see cref="INavigationService"/> for <see cref="ShellNavigationService"/>
    /// </summary>
    public partial class ShellPrismNavigationService : INavigationService
    {
        private readonly IContainerExtension _container;
        private IPageBehaviorFactory _pageBehaviorFactory { get; }

        Page CurrentPage => (CurrentShell?.CurrentItem?.CurrentItem as IShellSectionController)?.PresentedPage;

        Shell CurrentShell => _shell.Value;

        Lazy<Shell> _shell;
        /// <summary>
        /// Creates an instance of <see cref="ShellPrismNavigationService"/>
        /// </summary>
        /// <param name="containerExtension"></param>
        /// <param name="pageBehaviorFactory"></param>
        public ShellPrismNavigationService(
            IContainerExtension containerExtension,
            IPageBehaviorFactory pageBehaviorFactory)
        {
            _container = containerExtension;
            _pageBehaviorFactory = pageBehaviorFactory;

            _shell = new Lazy<Shell>(() =>
            {
                Shell.Current.Navigated += OnNavigated;
                Shell.Current.Navigating += OnNavigating;
                return Shell.Current;
            });
        }


        // you could also create prism versions of all the shell elements
        // <prism:PrismFlyouutItem
        IEnumerable<BaseShellItem> AllTheKingsItems()
        {
            var items = CurrentShell.Items;
            var sections = items.SelectMany(x => x.Items);
            var contents = sections.SelectMany(x => x.Items);

            return 
                items.OfType<BaseShellItem>()
                    .Union(sections)
                    .Union(contents);
        }

        void WireUpTemplates()
        {
            foreach(var item in AllTheKingsItems().OfType<ShellContent>())
            {
                var closure = Routing.GetRoute(item);
                if (String.IsNullOrWhiteSpace(closure))
                    throw new Exception("Everything needs a route");

                if(item.ContentTemplate == null)
                {
                    item.ContentTemplate =
                        new DataTemplate(() =>
                        {
                            return CreatePageFromSegment(closure);
                        });
                }
            }
        }

        void OnNavigating(object sender, ShellNavigatingEventArgs e)
        {
            WireUpTemplates();
        }

        void OnNavigated(object sender, ShellNavigatedEventArgs e)
        {
            PageUtilities.OnNavigatedFrom(_currentPage, _currentParameters);
            PageUtilities.OnNavigatedTo(CurrentPage, _currentParameters);
        }


        public class PrismTypeFactory : Xamarin.Forms.RouteFactory
        {
            IContainerExtension _container;
            string _segmentName;

            public PrismTypeFactory(IContainerExtension containerExtension, string segmentName)
            {
                _container = containerExtension;
                _segmentName = segmentName;
            }

            public override Element GetOrCreate()
            {
                try
                {
                    return _container.Resolve<object>(_segmentName) as Page;
                }
                catch (Exception ex)
                {
                    if (((IContainerRegistry)_container).IsRegistered<object>(_segmentName))
                        throw new NavigationException(NavigationException.ErrorCreatingPage, null, ex);

                    throw new NavigationException(NavigationException.NoPageIsRegistered, null, ex);
                }
            }
        }

        protected virtual Page CreatePage(string segmentName)
        {
            try
            {
                return _container.Resolve<object>(segmentName) as Page;
            }
            catch (Exception ex)
            {
                if (((IContainerRegistry)_container).IsRegistered<object>(segmentName))
                    throw new NavigationException(NavigationException.ErrorCreatingPage, null, ex);

                throw new NavigationException(NavigationException.NoPageIsRegistered, null, ex);
            }
        }

        protected virtual Page CreatePageFromSegment(string segment)
        {
            string segmentName = null;
            try
            {
                segmentName = UriParsingHelper.GetSegmentName(segment);
                var page = CreatePage(segmentName);
                if (page == null)
                {
                    var innerException = new NullReferenceException(string.Format("{0} could not be created. Please make sure you have registered {0} for navigation.", segmentName));
                    throw new NavigationException(NavigationException.NoPageIsRegistered, null, innerException);
                }

                PageUtilities.SetAutowireViewModelOnPage(page);
                _pageBehaviorFactory.ApplyPageBehaviors(page);

                // Not Relavent for Shell since we only work with Content Page and not Tabbed or Carousel Pages
                //ConfigurePages(page, segment);

                return page;
            }
            catch (NavigationException)
            {
                throw;
            }
            catch (Exception e)
            {
#if DEBUG
                Console.WriteLine(e);
                System.Diagnostics.Debugger.Break();
#endif
                throw;
            }
        }

        Task<INavigationResult> INavigationService.GoBackAsync()
        {
            throw new NotImplementedException();
        }

        Task<INavigationResult> INavigationService.GoBackAsync(INavigationParameters parameters)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Initiates navigation to the target specified by the <paramref name="uri"/>.
        /// </summary>
        /// <param name="uri">The Uri to navigate to</param>
        /// <remarks>Navigation parameters can be provided in the Uri and by using the <paramref name="parameters"/>.</remarks>
        /// <example>
        /// Navigate(new Uri("MainPage?id=3&amp;name=dan", UriKind.RelativeSource), parameters);
        /// </example>
        /// <returns>The <see cref="INavigationResult" /> which will provide a Success == <c>true</c> if the Navigation was successful.</returns>
        public virtual Task<INavigationResult> NavigateAsync(Uri uri) =>
            NavigateAsync(uri, null);

        Page _currentPage;
        INavigationParameters _currentParameters;
        /// <summary>
        /// Initiates navigation to the target specified by the <paramref name="uri"/>.
        /// </summary>
        /// <param name="uri">The Uri to navigate to</param>
        /// <param name="parameters">The navigation parameters</param>
        /// <remarks>Navigation parameters can be provided in the Uri and by using the <paramref name="parameters"/>.</remarks>
        /// <example>
        /// Navigate(new Uri("MainPage?id=3&amp;name=dan", UriKind.RelativeSource), parameters);
        /// </example>
        /// <returns>The <see cref="INavigationResult" /> which will provide a Success == <c>true</c> if the Navigation was successful.</returns>
        public virtual async Task<INavigationResult> NavigateAsync(Uri uri, INavigationParameters parameters)
        {
            try
            {
                WireUpTemplates();
                _currentParameters = parameters;
                _currentPage = CurrentPage;
                var navigationSegments = UriParsingHelper.GetUriSegments(uri);
                var navUri = String.Join("/", 
                    navigationSegments
                        .Select(x=> UriParsingHelper.GetSegmentName(x))
                        .ToArray()
                    );

                // just see if the first route is somewhere 
                // in the shell
                foreach (var ns in navigationSegments.Take(1))
                {
                    string segName = UriParsingHelper.GetSegmentName(ns);
                    foreach (var item in AllTheKingsItems())
                    {
                        if (Routing.GetRoute(item) == segName)
                        {
                            navigationSegments.Dequeue();
                            break;
                        }
                    }
                }

                foreach (var ns in navigationSegments.ToList())
                {
                    Routing.RegisterRoute(UriParsingHelper.GetSegmentName(ns), new PrismTypeFactory(_container, ns));
                }

                var pantalooons = UriParsingHelper.GetUriSegments(uri).ToList();

                // this means a shell item matched
                if (navigationSegments.Count != pantalooons.Count)
                    navUri = $"//{navUri}";
                    
                await CurrentShell.GoToAsync(navUri);
                return new NavigationResult()
                {
                    Success = true
                };
            }
            catch (NavigationException navEx)
            {
                return new NavigationResult
                {
                    Success = false,
                    Exception = navEx
                };
            }
            catch (Exception ex)
            {
                return new NavigationResult
                {
                    Success = false,
                    Exception = new NavigationException(NavigationException.UnknownException, null, ex)
                };
            }

        }

        /// <summary>
        /// Initiates navigation to the target specified by the <paramref name="name"/>.
        /// </summary>
        /// <param name="name">The name of the target to navigate to.</param>
        /// <returns>The <see cref="INavigationResult" /> which will provide a Success == <c>true</c> if the Navigation was successful.</returns>
        public virtual Task<INavigationResult> NavigateAsync(string name) =>
            NavigateAsync(name, null);

        /// <summary>
        /// Initiates navigation to the target specified by the <paramref name="name"/>.
        /// </summary>
        /// <param name="name">The name of the target to navigate to.</param>
        /// <param name="parameters">The navigation parameters</param>
        /// <returns>The <see cref="INavigationResult" /> which will provide a Success == <c>true</c> if the Navigation was successful.</returns>
        public virtual Task<INavigationResult> NavigateAsync(string name, INavigationParameters parameters) =>
            NavigateAsync(UriParsingHelper.Parse(name), parameters);
    }
}
