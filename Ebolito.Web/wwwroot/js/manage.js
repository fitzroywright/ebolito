(() => {
  const $ = (selector) => document.querySelector(selector);
  const professionalId = $('[data-professional-id]');
  const adminKey = $('[data-admin-key]');
  const globalStatus = $('[data-global-status]');
  const profileForm = $('[data-profile-form]');
  const projectForm = $('[data-project-form]');
  const uploadForm = $('[data-upload-form]');
  const projectList = $('[data-project-list]');
  const uploadedUrl = $('[data-uploaded-url]');
  const uploadPreview = $('[data-upload-preview]');
  let loadedProjects = [];

  const headers = (json = true) => {
    const result = {};
    const session = localStorage.getItem('ebolito.professionalSession');
    if (session) result['X-Ebolito-Professional-Session'] = session;
    else if (adminKey.value.trim()) result['X-Ebolito-Profile-Admin-Key'] = adminKey.value.trim();
    if (json) result['Content-Type'] = 'application/json';
    return result;
  };

  async function readResponse(response) {
    if (response.status === 204) return null;
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) {
      if (response.status === 401 && localStorage.getItem('ebolito.professionalSession')) throw new Error('Your professional session is missing, expired, or does not match this professional. Sign in again.');
      const message = body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`);
      throw new Error(message);
    }
    return body;
  }

  const requireAccess = () => {
    const id = professionalId.value.trim();
    if (!id) throw new Error('Professional ID is required.');
    if (!localStorage.getItem('ebolito.professionalSession') && !adminKey.value.trim()) throw new Error('Sign in as the professional or provide the administration key.');
    return id;
  };
  const parseSkillIds = (value) => value.split(',').map(x => x.trim()).filter(Boolean);
  const parseAreas = (value) => value.split(/\r?\n/).map(x => x.trim()).filter(Boolean).map(line => { const [parish, community] = line.split('|').map(x => x.trim()); return { parish, community: community || null }; });
  const parsePhotos = (value) => value.split(/\r?\n/).map(x => x.trim()).filter(Boolean).map((url, i) => ({ url, caption: null, sortOrder: i }));

  function setProfile(pro) {
    profileForm.slug.value = pro.slug || ''; profileForm.displayName.value = pro.displayName || ''; profileForm.businessName.value = pro.businessName || '';
    profileForm.headline.value = pro.headline || ''; profileForm.about.value = pro.about || ''; profileForm.phoneNumber.value = pro.phoneNumber || '';
    profileForm.whatsAppNumber.value = pro.whatsAppNumber || ''; profileForm.skillIds.value = (pro.skillIds || []).join(', ');
    profileForm.serviceAreas.value = (pro.serviceAreas || []).map(x => x.community ? `${x.parish}|${x.community}` : x.parish).join('\n'); profileForm.isActive.checked = pro.isActive !== false;
  }

  function renderProjects() {
    if (!loadedProjects.length) { projectList.innerHTML = '<p>No portfolio projects yet.</p>'; return; }
    projectList.innerHTML = '';
    loadedProjects.forEach(project => {
      const article = document.createElement('article'); const title = document.createElement('strong'); title.textContent = project.title;
      const meta = document.createElement('p'); meta.textContent = `${project.location}${project.completedOn ? ` • ${project.completedOn}` : ''}${project.isFeatured ? ' • Featured' : ''}`;
      const edit = document.createElement('button'); edit.type = 'button'; edit.textContent = 'Edit'; edit.addEventListener('click', () => editProject(project));
      const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = 'Delete'; remove.addEventListener('click', () => deleteProject(project.id));
      article.append(title, meta, edit, document.createTextNode(' '), remove); projectList.appendChild(article);
    });
  }

  function editProject(project) {
    projectForm.id.value = project.id || ''; projectForm.title.value = project.title || ''; projectForm.description.value = project.description || '';
    projectForm.location.value = project.location || ''; projectForm.completedOn.value = project.completedOn || ''; projectForm.skillIds.value = (project.skillIds || []).join(', ');
    projectForm.photos.value = (project.photos || []).sort((a, b) => a.sortOrder - b.sortOrder).map(x => x.url).join('\n'); projectForm.isFeatured.checked = !!project.isFeatured;
    projectForm.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  async function loadSite() {
    globalStatus.textContent = 'Loading…';
    try {
      const id = requireAccess();
      const data = await readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/site`, { headers: headers(false) }));
      setProfile(data.professional); loadedProjects = data.projects || []; renderProjects(); globalStatus.textContent = `Loaded ${data.professional.displayName}.`;
    } catch (error) { globalStatus.textContent = error.message; }
  }

  $('[data-load-site]').addEventListener('click', loadSite);
  profileForm.addEventListener('submit', async event => {
    event.preventDefault(); const status = $('[data-profile-status]'); status.textContent = 'Saving…';
    try {
      const id = requireAccess();
      const payload = { slug: profileForm.slug.value.trim(), displayName: profileForm.displayName.value.trim(), businessName: profileForm.businessName.value.trim() || null, headline: profileForm.headline.value.trim(), about: profileForm.about.value.trim(), phoneNumber: profileForm.phoneNumber.value.trim() || null, whatsAppNumber: profileForm.whatsAppNumber.value.trim() || null, skillIds: parseSkillIds(profileForm.skillIds.value), serviceAreas: parseAreas(profileForm.serviceAreas.value), isActive: profileForm.isActive.checked };
      const saved = await readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/site`, { method: 'PUT', headers: headers(), body: JSON.stringify(payload) }));
      setProfile(saved); status.textContent = 'Profile saved.';
    } catch (error) { status.textContent = error.message; }
  });

  uploadForm.addEventListener('submit', async event => {
    event.preventDefault(); const status = $('[data-upload-status]'); status.textContent = 'Uploading…';
    try {
      const id = requireAccess(); const form = new FormData(uploadForm);
      const saved = await readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/portfolio-media`, { method: 'POST', headers: headers(false), body: form }));
      uploadedUrl.value = saved.url; uploadPreview.src = saved.url; uploadPreview.hidden = false; status.textContent = 'Image uploaded. The URL can now be added to a project.';
      if (!projectForm.photos.value.trim()) projectForm.photos.value = saved.url; else projectForm.photos.value += `\n${saved.url}`;
    } catch (error) { status.textContent = error.message; }
  });

  projectForm.addEventListener('submit', async event => {
    event.preventDefault(); const status = $('[data-project-status]'); status.textContent = 'Saving…';
    try {
      const id = requireAccess();
      const payload = { id: projectForm.id.value || null, title: projectForm.title.value.trim(), description: projectForm.description.value.trim(), location: projectForm.location.value.trim(), completedOn: projectForm.completedOn.value || null, skillIds: parseSkillIds(projectForm.skillIds.value), photos: parsePhotos(projectForm.photos.value), isFeatured: projectForm.isFeatured.checked };
      const saved = await readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/portfolio`, { method: 'POST', headers: headers(), body: JSON.stringify(payload) }));
      const index = loadedProjects.findIndex(x => x.id === saved.id); if (index >= 0) loadedProjects[index] = saved; else loadedProjects.push(saved);
      renderProjects(); editProject(saved); status.textContent = 'Project saved.';
    } catch (error) { status.textContent = error.message; }
  });

  async function deleteProject(projectId) {
    const status = $('[data-project-status]'); status.textContent = 'Deleting…';
    try {
      const id = requireAccess(); const response = await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/portfolio/${encodeURIComponent(projectId)}`, { method: 'DELETE', headers: headers(false) });
      await readResponse(response); loadedProjects = loadedProjects.filter(x => x.id !== projectId); renderProjects(); clearProject(); status.textContent = 'Project deleted.';
    } catch (error) { status.textContent = error.message; }
  }

  function clearProject() { projectForm.reset(); projectForm.id.value = ''; $('[data-project-status]').textContent = ''; }
  $('[data-clear-project]').addEventListener('click', clearProject);

  const remembered = localStorage.getItem('ebolito.professionalId');
  if (remembered) professionalId.value = remembered;
  if (remembered && localStorage.getItem('ebolito.professionalSession')) loadSite();
})();
