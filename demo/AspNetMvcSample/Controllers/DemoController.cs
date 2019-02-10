using AspNetMvcSample.Models;
using System.Web.Mvc;

namespace AspNetMvcSample.Controllers
{
    public class DemoController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult Get(DemoFormModel model)
        {
            return View(model);
        }

        [HttpPost]
        public ActionResult Post(DemoFormModel model)
        {
            return View(model);
        }
    }
}