using AspNetCoreMvcSample.Models;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreMvcSample.Controllers
{
    public class DemoController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Get([FromQuery]DemoFormModel model)
        {
            return View(model);
        }

        [HttpPost]
        public IActionResult Post([FromForm]DemoFormModel model)
        {
            return View(model);
        }
    }
}