﻿using KozossegiAPI.DTOs;
using KozossegiAPI.Models;
using System.Net;

namespace KozossegiAPI.Repo
{
    public interface IStudyRepository : IGenericRepository<Study>
    {
        Task<HttpStatusCode> UpdateStudies(List<StudyDto> changes);
    }
}
