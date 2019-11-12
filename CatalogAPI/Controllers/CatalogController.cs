using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CatalogAPI.Helpers;
using CatalogAPI.Infrastructure;
using CatalogAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace CatalogAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
   // [EnableCors("AllowPartners")]
  // [Authorize]
    public class CatalogController : ControllerBase
    {
        private CatalogContext db;
        private IConfiguration config;
        public CatalogController(CatalogContext db, IConfiguration configuration)
        {
            this.db = db;
            this.config = configuration;
        }

        [AllowAnonymous]
        [HttpGet("", Name ="GetProducts")]
        public async Task<ActionResult<List<CatalogItem>>> GetProducts()
        {
         var result = await  this.db.Catalog.FindAsync<CatalogItem>(FilterDefinition<CatalogItem>.Empty);
          return result.ToList();
        }

        [Authorize(Roles ="admin")]
        [HttpPost("", Name = "AddProduct")]
        [ProducesResponseType((int)HttpStatusCode.Created)] // Now swager can identify the status code as 201
        [ProducesResponseType((int)HttpStatusCode.BadRequest)] // and 400
        public ActionResult<CatalogItem> AddProduct(CatalogItem item)
        {
            TryValidateModel(item);  // Explicitly validate model
            if (ModelState.IsValid)
            {
                this.db.Catalog.InsertOne(item);
                return Created("", item); // status code 201
            }
            else {
                return BadRequest(ModelState); // status code 400
               
            }
        }

        [AllowAnonymous]
        [HttpGet("{id}", Name = "FindById")]
        [ProducesResponseType((int)HttpStatusCode.NotFound)] // 404
        [ProducesResponseType((int)HttpStatusCode.OK)] // 200
        public async Task<ActionResult<CatalogItem>> FindProductById(string id)
        {
            var builder = Builders<CatalogItem>.Filter;
            var filter = builder.Eq("Id", id);
            var item = await this.db.Catalog.FindAsync(filter);
            if (item == null)
            {
                return NotFound(); // Not found, status code is 404
            }
            else
            {
                return Ok(item); // Found, status code is 200
            }
            
        }

        [Authorize(Roles = "admin")]
        [HttpPost("product")]
        public ActionResult<CatalogItem> AddProductWithImage()
        {
            // var imageName = SaveImageToLocal(Request.Form.Files[0]);
            var imageName = SaveImageToCloudAsync(Request.Form.Files[0]).GetAwaiter().GetResult();
          
            var catalogItem = new CatalogItem()
            {
                Name = Request.Form["name"],
                Price = Double.Parse(Request.Form["price"]),
                Quantity = Int32.Parse(Request.Form["quantity"]),
                ReorderLevel = Int32.Parse(Request.Form["reorderLevel"]),
                ManufacturingDate = DateTime.Parse(Request.Form["manufacturingDate"]),
                Vendors = new List<Vendor>(),
                ImageUrl = imageName
            };
            db.Catalog.InsertOne(catalogItem);
            //Back up to azure table storage;
            BackupToTableAsync(catalogItem).GetAwaiter().GetResult();
            return catalogItem;
        }

        [NonAction]
        private string SaveImageToLocal(IFormFile image)
        {
            var imageName = $"{Guid.NewGuid()}_{image.FileName}";
            var dirName = Path.Combine(Directory.GetCurrentDirectory(), "Images");
            if (!Directory.Exists(dirName))
            {
                Directory.CreateDirectory(dirName);
            }
            var filePath = Path.Combine(dirName, imageName);
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                image.CopyTo(fs);
            }
            return $"/Images/{imageName}";
        }

        [NonAction]
        private async Task<string> SaveImageToCloudAsync(IFormFile image)
        {
            var imageName = $"{Guid.NewGuid()}_{image.FileName}";
            var tempFile = Path.GetTempFileName();
            using (FileStream fs = new FileStream(tempFile, FileMode.Create))  // uploading the file to the tempfile
            {
               await image.CopyToAsync(fs);
            }

            var imageFile = Path.Combine(Path.GetDirectoryName(tempFile),imageName);  // rename the tempfile name to real file name
            System.IO.File.Move(tempFile, imageFile);   // Move() function is use to rename file
            StorageAccountHelper storageHelper = new StorageAccountHelper();
            storageHelper.StorageConnectionString = config.GetConnectionString("StorageConnection");
            var fileUri = await storageHelper.UploadFileToBlobAsync(imageFile, "eshopimages");
            System.IO.File.Delete(imageFile); // to delete the tempFolder after image get uploaded on cloud
            return fileUri;
        }

        [NonAction]
        private async Task<CatalogEntity> BackupToTableAsync(CatalogItem item)
        {
            StorageAccountHelper storageHelper = new StorageAccountHelper();
            storageHelper.TableConnetionString = config.GetConnectionString("TableConnection");
            return await storageHelper.SaveToTableAsync(item);
        }

    }
}