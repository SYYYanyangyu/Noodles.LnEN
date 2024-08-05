using Listening.Admin.WebAPI.Categories.Request;
using Listening.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Noodles.Common.Validators;
using IListeningRepository = Listening.Domain.IListeningRepository;

namespace Listening.Admin.WebAPI.Categories;

[Route("[controller]/[action]")]
[Authorize(Roles = "Admin")]
[ApiController]
[UnitOfWork(typeof(ListeningDbContext))]
public class CategoryController : ControllerBase
{
    private IListeningRepository repository;
    private readonly ListeningDbContext dbContext;
    private readonly ListeningDomainService domainService;
    public CategoryController(ListeningDbContext dbContext, ListeningDomainService domainService, IListeningRepository repository)
    {
        this.dbContext = dbContext;
        this.domainService = domainService;
        this.repository = repository;
    }

    [HttpGet]
    public Task<Category[]> FindAll()
    {
        return repository.GetCategoriesAsync();
    }

    [HttpGet]
    [Route("{id}")]
    public async Task<ActionResult<Category?>> FindById([RequiredGuid] Guid id)
    {
        //返回ValueTask的需要await的一下
        var cat = await repository.GetCategoryByIdAsync(id);
        if (cat == null)
        {
            return NotFound($"没有Id={id}的Category");
        }
        else
        {
            return cat;
        }
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Add(CategoryAddRequest req)
    {
        try
        {
            var category = await domainService.AddCategoryAsync(req.Name, req.CoverUrl, req.path);
            dbContext.Add(category);
            return category.Id;
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }

    [HttpPut]
    [Route("{id}")]
    public async Task<ActionResult> Update([RequiredGuid] Guid id, CategoryUpdateRequest request)
    {
        var cat = await repository.GetCategoryByIdAsync(id);
        if (cat == null)
        {
            return NotFound("id不存在");
        }
        cat.ChangeName(request.Name);
        cat.ChangeCoverUrl(request.CoverUrl);
        cat.ChangeTime();
        return Ok();
    }

    [HttpDelete]
    [Route("{id}")]
    public async Task<ActionResult> DeleteById([RequiredGuid] Guid id)
    {
        var cat = await repository.GetCategoryByIdAsync(id);
        if (cat == null)
        {
            //这样做仍然是幂等的，因为“调用N次，确保服务器处于与第一次调用相同的状态。”与响应无关
            return NotFound($"没有Id={id}的Category");
        }
        cat.SoftDelete();//软删除
        return Ok();
    }

    [HttpPut]
    public async Task<ActionResult> Sort(CategoriesSortRequest req)
    {
        await domainService.SortCategoriesAsync(req.SortedCategoryIds);
        return Ok();
    }
}