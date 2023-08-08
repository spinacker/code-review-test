using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static BookingWebsite.Api.Public.Controllers.TestReview;

namespace BookingWebsite.Api.Public.Controllers;

public class TestReview
{
    /*
     * 1. Вынести все классы в отдельные файлы
     * 2. Разделить на несколько проектов - Domain, Infrastructure, Application, API
     * 3. Нужна ли авторизация/аутентификация?
     */

    /*
        Выделить интерфейс IApplicationDbContext
    */
    public interface IApplicationDbContext
    {
        DbSet<User> Users { get; } 
        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    }

    /*
        Реализовать IApplicationDbContext в классе
    */
    public class ApplicationDbContext : IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            /* реализация сохранения */
        }
    }

    /*
       Конфигурацию DbContext вынести в инициализацию сервиса
     */
    public void ConfigureServices(IServiceCollection services)
    {

        services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(Configuration.GetConnectionString("AppConnectionString"))); // строку подключения вынести в настройки

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>()); // работа с DBContext через DI
    }

    /* я бы для контроллеров использовал библиотеку Mediatr в базовом классе.  Все контроллеры наследовать от ApiControllerBase  */
    /*
    [ApiController]
    [Route("api/[controller]")]
    public abstract class ApiControllerBase : ControllerBase
    {
        private ISender _mediator = null!;
        protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
    }


        в итоге все методы в контроллерах были бы примерно такими
        public async Task<ActionResult<Users>>> Get([FromQuery] GetUsersQuery query)
        {
            return await Mediator.Send(query);
        }
        в классе GetUsersQuery - реализуется вчя логика выборки пользователей

     */
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly IUserService userService;

        public UsersController(IUserService userService) // инжектим UserService через конструктор
        { 
            this.userService = userService;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken = default) // название методов по их 
        {
            //var userService = new UserService(); // сервис берем из DI
            var users = await userService.GetAllUsersAsync(cancellationToken);
            return new OkObjectResult(users);
        }

        [HttpGet("{id}")] //параметр из адресной строки
        public async Task<IActionResult> Get(string id, CancellationToken cancellationToken = default)
        {
            //var userService = new UserService(); // сервис берем из DI
            var user = await userService.FindUserAsync(id, cancellationToken);
            return new OkObjectResult(user);
        }
    }

    public interface IUserService // выделяем интерфейс
    {
        Task<IEnumerable<User>> GetAllUsersAsync(CancellationToken cancellationToken); // добавить в название Async
        Task<User> FindUserAsync(string id, CancellationToken cancellationToken);
    }

    public interface IExternalUserService
    {
        Task<string> GetUserLinkAsync(long id, CancellationToken cancellationToken);
    }

    public class ExternalUserService : IExternalUserService
    {
        public async Task<string> GetUserLinkAsync(long id, CancellationToken cancellationToken = default)
        {
            var client = new HttpClient();

            try
            {
                var result = await client.GetAsync($"https//yandex.ru/users/{id}", cancellationToken); // URL вынести в настройки
                return await result.Content.ReadAsStringAsync(cancellationToken);
            }
            catch { } // добавить обработку исключений и логику дефолтного значения, если сервис недоступен или не вернул значение

            return string.Empty;
        }
    }

    public class UserService : IUserService 
    {
        //public static string ConnectionString; // строку подключения выносим в настройки

        private readonly IApplicationDbContext dbContext;
        private readonly IExternalUserService externalUserService;

        public UserService(IApplicationDbContext dbContext, IExternalUserService externalUserService)
        { 
            this.dbContext = dbContext;
            this.externalUserService = externalUserService;
        }

        /* всю логику получения ExternalLink перенест в метод создания User, если это возможно*/
        private async Task UpdateUsersExternalLinkAsync(IEnumerable<User> users, CancellationToken cancellationToken = default) // добавить CancellationToken
        {
            /* при использовании Parallel.ForEach я бы добавил ограничение на максимальное количество параллельных потоков, для того, что бы избежать ситуации, когда все потоки заняты 
             * и система подвисает */
            Parallel.ForEach(users, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async user => {
                /* лучше всего, всю логику получения ExternalLink перенести в метод создания User*/
                // добавить проверку, если уже есть  ExternalLink, нужно ли повторно запрашивать?
                if (string.IsNullOrEmpty(user.ExternalLink))
                {
                    // работу с внешним сервисом перенести в отдельный сервисный класс, например ExternalUserService и добавить обработку ошибок
                    user.ExternalLink = await externalUserService.GetUserLinkAsync(user.Id, cancellationToken);
                }
                /* */
            });
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync(CancellationToken cancellationToken = default) // добавить CancellationToken
        {
            /* инициализация через DI*/
            //var optionsBuilder = new DbContextOptionsBuilder<DbContext>();
            //optionsBuilder.UseNpgsql(ConnectionString);
            //var dbContext = new DbContext(optionsBuilder.Options);

            var users = dbContext.Users;

            /* в данном случае имеет SideEffect - запрашиваем пользователей, а еще по ходу получаем запрос к внешней системе на ExternalLink и сохранение в базу*/
            /* очень дорогостоящая операция */
            //Parallel.ForEach(users, async user => {
            //        // работу с внешним сервисом перенести в отдельный сервисный класс, например ExternalUserService и добавить обработку ошибок
            //        user.ExternalLink = this.externalUserService.GetUser(user.Id);
            //});
            // Если логика все таки нужна именно такая
            await UpdateUsersExternalLinkAsync(dbContext.Users, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            return users;
        }

        /* убрать метод, так как он не несет никакого функционала*/
        //public IEnumerable<User> GetUsers()
        //{
        //    /* инициализация через DI*/
        //    //var optionsBuilder = new DbContextOptionsBuilder<DbContext>();
        //    //optionsBuilder.UseNpgsql(ConnectionString);
        //    //var dbContext = new DbContext(optionsBuilder.Options);

        //    return dbContext.Users;
        //}

        public async Task<User> FindUserAsync(string id, CancellationToken cancellationToken = default) // добавить асинхронность
        {
            return await dbContext.Users.FindAsync(id); // Find работает быстрее
            //return dbContext.Users.Where(x => x.Id.ToString() == id).First();
        }
    }

    /* Доменная сущность - перенести в проект Domain*/
    public class User
    {
        [Key]
        public long Id { get; set; }  // странный User без UserName !!!!
        public string ExternalLink { get; set; }
    }
}