window.posterDraft = {
  load: function () {
    return sessionStorage.getItem("poster-request-draft");
  },
  save: function (value) {
    sessionStorage.setItem("poster-request-draft", value);
  },
  clear: function () {
    sessionStorage.removeItem("poster-request-draft");
  }
};
