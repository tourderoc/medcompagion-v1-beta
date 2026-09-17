namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Repagination dans le navigateur — le dernier mot sur la mise en page.
    ///
    /// POURQUOI PAS UNE ESTIMATION EN C#. Le 17/09/2026, j'ai calibré deux estimateurs de
    /// hauteur sur des mesures réelles. Les deux se sont trompés : celui de la cartographie
    /// surestimait de 6 % (découpages inutiles), celui de la synthèse sous-estimait de 21 %
    /// (page coupée quand même). Et la conclusion « le Projet thérapeutique a de la marge »,
    /// tirée de sept dossiers du poste, s'est effondrée au premier dossier réellement rempli :
    /// trois de ses pages dépassaient, jusqu'à +76 mm — une quinzaine de lignes perdues.
    ///
    /// La leçon est nette : une hauteur de texte ne se calcule pas hors du moteur qui la rend.
    /// Ce script travaille donc là où la mesure est exacte, dans le navigateur, et il tourne
    /// dans les deux contextes qui comptent — l'aperçu (WebView2) et l'export PDF (Edge
    /// headless), qui sont le même moteur.
    ///
    /// CE QU'IL FAIT. Pour chaque page bâtie sur des cartes, il les retire, puis les remet une
    /// par une en mesurant après chaque ajout. Dès qu'une carte ferait dépasser l'A4, elle
    /// ouvre une page suivante — un clone de l'ossature (en-tête, légende, pied) avec
    /// « (suite) » au sous-titre. Une carte n'est jamais coupée en deux.
    ///
    /// CE QU'IL NE FAIT PAS. Il ne touche pas aux pages dont le contenu ne varie pas — la
    /// couverture est un gabarit, l'annexe méthodologique une ressource figée — ni aux pages
    /// qui n'ont qu'une seule carte : la déplacer ne servirait à rien, elle déborderait pareil
    /// sur la page suivante. Ces cas restent signalés par la phase 1 du contrôle qualité.
    ///
    /// La pagination faite en C# (cartographie, synthèse 5.2, annexe contacts) est conservée :
    /// elle donne un meilleur point de départ, et elle reste le seul filet si le script ne
    /// s'exécute pas.
    /// </summary>
    public partial class RestitutionHtmlPreviewService
    {
        private const string ScriptRepagination = @"
<script>
(function () {
  var A4 = 297 / 25.4 * 96;          // 1122,5 px
  var GARDE = 4;                      // tolérance d'arrondi du moteur de rendu
  var SELECTEUR_CARTES = '.pt-card, .sd-card, .pc-card, .ce-card, .ac-card';

  // Hauteur réelle du contenu : le plancher min-height: 297mm (et la hauteur fixe imposée
  // en media print) est neutralisé le temps de la mesure, sinon toute page mesurerait
  // exactement 297 mm et un débordement serait invisible.
  function hauteurReelle(page) {
    var mh = page.style.minHeight, hh = page.style.height, ov = page.style.overflow;
    page.style.minHeight = '0'; page.style.height = 'auto'; page.style.overflow = 'visible';
    var h = page.getBoundingClientRect().height;
    page.style.minHeight = mh; page.style.height = hh; page.style.overflow = ov;
    return h;
  }

  function estFigee(page) {
    return page.classList.contains('cover-page') || page.classList.contains('annexe-page');
  }

  // Le conteneur des cartes est leur parent commun — il varie selon la page
  // (.pt-cards-wrapper, la page elle-même, un bloc intermédiaire).
  function conteneurDeCartes(page) {
    var cartes = page.querySelectorAll(SELECTEUR_CARTES);
    if (cartes.length < 2) return null;
    var parent = cartes[0].parentNode;
    for (var i = 1; i < cartes.length; i++) {
      if (cartes[i].parentNode !== parent) return null;   // cartes imbriquées : on s'abstient
    }
    return parent;
  }

  function marquerSuite(page) {
    var st = page.querySelector('.pc-subtitle, .an-header-sub, .ac-header-sub');
    if (st && st.textContent.indexOf('(suite)') < 0) st.textContent = st.textContent + ' (suite)';
  }

  // Ossature vide : un clone du modèle, éléments de flux retirés, inséré après « apres ».
  // Le modèle est toujours la page d'origine — on ne clone jamais une page déjà remplie,
  // sinon la troisième page reprendrait le contenu de la deuxième.
  function nouvellePageDepuis(modele, apres) {
    var clone = modele.cloneNode(true);
    var cc = clone.querySelectorAll(SELECTEUR_CARTES);
    for (var i = 0; i < cc.length; i++) cc[i].parentNode.removeChild(cc[i]);
    marquerSuite(clone);
    apres.parentNode.insertBefore(clone, apres.nextSibling);
    return clone;
  }

  function conteneurEquivalent(page, conteneurOrigine, pageOrigine) {
    // Même position dans l'arbre que le conteneur d'origine : on le retrouve par son chemin.
    if (conteneurOrigine === pageOrigine) return page;
    var chemin = [];
    var n = conteneurOrigine;
    while (n && n !== pageOrigine) {
      chemin.unshift(Array.prototype.indexOf.call(n.parentNode.childNodes, n));
      n = n.parentNode;
    }
    var cible = page;
    for (var i = 0; i < chemin.length; i++) {
      cible = cible.childNodes[chemin[i]];
      if (!cible) return null;
    }
    return cible;
  }

  // Répartit une suite d'éléments frères sur autant de pages qu'il en faut. Générique :
  // au premier niveau les éléments sont des cartes, au second les blocs internes d'une
  // carte trop haute à elle seule.
  function repartir(page, conteneur, elements) {
    if (elements.length < 2) return [page];

    for (var i = 0; i < elements.length; i++) elements[i].parentNode.removeChild(elements[i]);

    var pageOrigine = page;
    var produites   = [page];
    var pageCourante = page;
    var contCourant  = conteneur;
    var surPage = 0;

    for (var j = 0; j < elements.length; j++) {
      contCourant.appendChild(elements[j]);
      surPage++;

      // Un seul élément sur la page : on le garde même s'il dépasse — le déplacer
      // reporterait le débordement sans le résoudre.
      if (surPage > 1 && hauteurReelle(pageCourante) > A4 + GARDE) {
        contCourant.removeChild(elements[j]);
        pageCourante = nouvellePageDepuis(pageOrigine, pageCourante);
        contCourant  = conteneurEquivalent(pageCourante, conteneur, pageOrigine);
        if (!contCourant) { pageCourante.parentNode.removeChild(pageCourante); return produites; }
        contCourant.appendChild(elements[j]);
        produites.push(pageCourante);
        surPage = 1;
      }
    }

    return produites;
  }

  function repaginer(page) {
    if (estFigee(page)) return;
    // Pas de « return » si la page n'a qu'une carte : c'est précisément le cas qui a besoin
    // du deuxième niveau. La page « Situation quotidienne et ressources » en est l'exemple —
    // une seule carte, débordant de 56 mm, que le premier niveau ne pouvait pas traiter.
    var pages = [page];
    var conteneur = conteneurDeCartes(page);

    if (conteneur && hauteurReelle(page) > A4 + GARDE) {
      var cartes = Array.prototype.slice.call(page.querySelectorAll(SELECTEUR_CARTES));
      pages = repartir(page, conteneur, cartes);
    }

    // Deuxième niveau : une page qui déborde encore avec UNE SEULE carte se découpe à
    // l'intérieur de cette carte, sur ses blocs internes. L'en-tête de la carte est
    // reproduit sur la suite — c'est le comportement d'un document, pas une anomalie.
    for (var k = 0; k < pages.length; k++) {
      var p = pages[k];
      if (hauteurReelle(p) <= A4 + GARDE) continue;

      var cartesP = p.querySelectorAll(SELECTEUR_CARTES);
      if (cartesP.length !== 1) continue;

      var corps = cartesP[0].querySelector('.pc-card-body, .pt-card-body, .sd-card-body, .ce-card-body');
      if (!corps) continue;

      var blocs = Array.prototype.slice.call(corps.children);
      repartir(p, corps, blocs);
    }
  }

  // Les numéros sont réécrits à la fin : le C# les avait posés sur un document qui comptait
  // moins de pages.
  function renumeroter() {
    var pages = document.querySelectorAll('.page');
    for (var i = 0; i < pages.length; i++) {
      var n = pages[i].querySelector('.pc-page-num');
      if (n) { n.textContent = 'PAGE ' + (i + 1) + '/' + pages.length; continue; }
      var d = pages[i].querySelector('.page-num');
      if (d) d.textContent = 'Page ' + (i + 1) + '/' + pages.length;
    }
  }

  function lancer() {
    try {
      // On itère sur un instantané : les pages créées sont déjà réparties par construction.
      var pages = Array.prototype.slice.call(document.querySelectorAll('.page'));
      for (var i = 0; i < pages.length; i++) repaginer(pages[i]);
      renumeroter();
      document.documentElement.setAttribute('data-repagine', '1');
    } catch (e) {
      // Sans repagination, le document reste celui que le C# a produit : des pages peuvent
      // être coupées à l'impression, mais rien n'est cassé. La phase 1 le dira.
      document.documentElement.setAttribute('data-repagine', 'erreur');
    }
  }

  // Les polices changent les retours à la ligne, donc les hauteurs : on les attend.
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(lancer);
  else window.addEventListener('load', lancer);
})();
</script>";
    }
}
