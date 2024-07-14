﻿using KozossegiAPI.Controllers.Cloud;
using KozoskodoAPI.DTOs;
using KozoskodoAPI.Models;
using Microsoft.AspNetCore.Mvc;
using KozoskodoAPI.Repo;
using KozossegiAPI.Models.Cloud;
using KozossegiAPI.Services;
using KozossegiAPI.Controllers.Cloud.Helpers;

namespace KozoskodoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class PostController : ControllerBase
    {
        public IStorageController? _storageController;
        public INotificationRepository _InotificationRepository;
        public IPostRepository<PostDto> _PostRepository;
        public IFileHandlerService _fileHandlerService;
        public PostController(

            IPostRepository<PostDto> postRepository,
            IFileHandlerService fileHandlerService,
            IStorageController? storageController = null,
            INotificationRepository notificationRepository = null)
        {
            _storageController = storageController;
            _InotificationRepository = notificationRepository;
            _PostRepository = postRepository;
            _fileHandlerService = fileHandlerService;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Post>> Get(int id)
        {
            //var res = await _context.Post.FindAsync(id);
            var res = await _PostRepository.GetByIdAsync<Post>(id);
            if (res != null)
            {
                return Ok(res);
            }
            return NotFound();
        }


        /// <summary>
        /// After authentication and checking the relation between the post viewer and the author of the post and the publicity of the post returns the actual posts.
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpGet("GetAllPost/{profileId}/{userId}/{currentPage}/")]
        [HttpGet("GetAllPost/{profileId}/{userId}/{currentPage}/{itemPerRequest}/")]
        public async Task<ContentDto<PostDto>> GetAllPost(int profileId, int userId, int currentPage = 1, int itemPerRequest = 10)
        {
            return await _PostRepository.GetAllPost(profileId, userId, currentPage, itemPerRequest);
        }

        [HttpGet("getPhotos/{profileId}/{cp}/{ipr}")]
        public async Task<ContentDto<PostDto>> GetImages(int profileId, int cp = 1, int ipr = 10)
        {
            var sortedItems = await _PostRepository.GetImages(profileId);
            if (sortedItems == null) return null;
            int totalPages = await _PostRepository.GetTotalPages(sortedItems, ipr);

            var returnValue = _PostRepository.Paginator(sortedItems, cp, ipr).ToList();

            return new ContentDto<PostDto>(returnValue, totalPages);
        }

        [HttpPost]
        [Route("createNew")]
        public async Task<ActionResult<Post>> Post([FromForm] CreatePostDto dto)
        {
            try
            {
                var user = await _PostRepository.GetByIdAsync<Personal>(dto.userId);
                if (user != null)
                {
                    Post newPost = new()
                    {
                        SourceId = dto.SourceId,
                        PostContent = dto.postContent,
                        Dislikes = 0,
                        Likes = 0
                    };

                    await _PostRepository.InsertSaveAsync<Post>(newPost);

                    bool isVideo = _fileHandlerService.FormatIsVideo(dto.Type);
                    if (isVideo || _fileHandlerService.FormatIsImage(dto.Type))
                    {
                        ContentType type = isVideo ? ContentType.Video : ContentType.Image;
                        if (dto.File != null)
                        {
                            MediaContent media = new(newPost.Id, dto.Name, type); //mentés az adatbázisba
                            FileUpload newFile = new FileUpload(dto.Name, dto.Type, dto.File); //mentés a felhőbe

                            var fileName = await _storageController.AddFile(newFile, KozossegiAPI.Controllers.Cloud.Helpers.BucketSelector.IMAGES_BUCKET_NAME); //Csak a fájl neve tér vissza
                            media.FileName = fileName.ToString();
                            await _PostRepository.InsertAsync<MediaContent>(media);
                            //await _context.SaveChangesAsync(); Elég a végén menteni most még...
                        }

                        //Create new junction table with user and postId
                        PersonalPost personalPost = new PersonalPost()
                        {
                            personId = dto.userId,
                            postId = newPost.Id
                        };
                        await _PostRepository.InsertSaveAsync<PersonalPost>(personalPost);
                    }

                    var closerFriends = _PostRepository.GetCloserFriendIds(dto.userId);
                    var stringLength = dto.postContent.Length > 60 ? dto.postContent.Take(50) + "..." : dto.postContent;

                    foreach (var friendId in closerFriends)
                    {
                        if (friendId != 0)
                        {
                            NotificationWithAvatarDto notificationWithAvatarDto =
                                new NotificationWithAvatarDto(
                                    friendId,
                                    user.id,
                                    user.avatar,
                                    dto.postContent,
                                    NotificationType.NewPost);
                            await _InotificationRepository.RealtimeNotification(friendId, notificationWithAvatarDto);
                            await _PostRepository.InsertAsync<Notification>(notificationWithAvatarDto);
                        }
                    }
                    await _PostRepository.SaveAsync();

                    HelperService service = new();
                    var postToReturn = new PostDto()
                    {
                        PostContent = newPost.PostContent,
                        PostId = newPost.Id,
                        AuthorId = dto.userId,
                        AuthorAvatar = user.avatar,
                        Dislikes = 0,
                        Likes = 0,
                        FullName = service.GetFullname(user.firstName, user.middleName, user.lastName),
                        DateOfPost = DateTime.Now,
                        PostComments = new()
                    };
                    return Ok(postToReturn);
                }
                return NotFound();
            }
            catch (Exception ex)
            {
                return BadRequest();
            }
        }

        //Gets the postId and the modified content of the post
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, [FromForm] CreatePostDto data)
        {
                var post = await _PostRepository.GetByIdAsync<Post>(id);
                if (post == null || post.Id != id)
                    return NotFound();

                //Modify only the content and the date of post
                post.PostContent = data.postContent;
                post.DateOfPost = DateTime.UtcNow;
                await _PostRepository.UpdateThenSaveAsync(post);
                return Ok();
        }

        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var post = await _PostRepository.GetByIdAsync<Post>(id);
            if (post == null)
                return NotFound();

            //Find the person in the personalPost junction table and remove the connection
            var personalPost = await _PostRepository.GetByIdAsync<PersonalPost>(post.Id);
            await _PostRepository.RemoveAsync<PersonalPost>(personalPost);
            await _PostRepository.RemoveAsync(post);
            await _PostRepository.SaveAsync();
            return Ok();
        }

        //Likes or dislikes a post.
        //Also increments or decrements the number of likes or dislikes
        /*
        [HttpPut("action")]
        public async Task<IActionResult> DoAction(Like_DislikeDto dto)
        {
            //TODO: This will be updated when the writing the tests are done. Make it work with comments also from commentcontroller.

            var post = await _PostRepository.GetByIdAsync<Post>(dto.postId);
            if (post == null) return NotFound();
            //TODO: Refactor into postDto and return also the userReaction
            var isUserDidAction = await _context.PostReaction.FirstOrDefaultAsync(u => u.PostId == dto.postId && u.UserId == dto.UserId);

            if (isUserDidAction == null)
            {
                PostReaction newReaction = new PostReaction()
                {
                    PostId = dto.postId,
                    UserId = dto.UserId,
                    ReactionType = dto.actionType
                };

                _context.PostReaction.Add(newReaction); //Creates a new entry and updates the post
                if (dto.actionType == "like")
                {
                    post.Likes++;
                }
                else if (dto.actionType == "dislike")
                {
                    post.Dislikes++;
                }
            }
            else
            {
                if (isUserDidAction.ReactionType == dto.actionType)
                { //Ha már ugyanaz az a művelet mint korábban akkor törlés
                    _context.PostReaction.Remove(isUserDidAction);
                    if (isUserDidAction.ReactionType == "like")
                    {
                        post.Likes--;
                    }
                    else if (isUserDidAction.ReactionType == "dislike")
                    {
                        post.Dislikes--;
                    }
                }
                if (isUserDidAction.ReactionType != dto.actionType)  //Ha már volt reakció, de más, akkor update
                {
                    isUserDidAction.ReactionType = dto.actionType == "like" ? "dislike" : "like";
                    _context.PostReaction.Update(isUserDidAction);
                    if (isUserDidAction.ReactionType == "like")
                    {
                        post.Likes--;
                        post.Dislikes++;
                    }
                    else if (isUserDidAction.ReactionType == "dislike")
                    {
                        post.Dislikes--;
                        post.Likes++;
                    }
                }
            }

            _context.Post.Update(post);
            await _context.SaveChangesAsync();
            return Ok(post);
        }

        */

    }

}