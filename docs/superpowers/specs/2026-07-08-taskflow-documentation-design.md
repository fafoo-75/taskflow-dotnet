# Spec : Documentation interactive TaskFlow

Date : 2026-07-08

## Contexte

TaskFlow est une application ASP.NET Core MVC (gestion de tâches) sans documentation utilisateur ni documentation technique formalisée au-delà du `CLAUDE.md` (guidance pour Claude Code, pas destiné aux utilisateurs finaux). Le but est de produire une documentation HTML autonome et interactive pour deux publics : les utilisateurs finaux de l'application et les développeurs qui la maintiendront.

## Objectif

Une page HTML unique, interactive, servie par l'application elle-même, qui documente :
1. L'usage fonctionnel de TaskFlow (guide utilisateur).
2. L'architecture technique du projet (documentation développeur).

## Format et emplacement

- Fichier unique **autonome** : `wwwroot/docs/index.html`. CSS et JS inline dans le fichier (pas de dépendance externe, pas de CDN), pour qu'il fonctionne sans build et sans connexion réseau.
- Servi automatiquement par ASP.NET Core (fichiers statiques de `wwwroot` déjà activés via `app.UseStaticFiles()` dans `Program.cs`) : accessible à `/docs/index.html`.
- Publié également en tant qu'Artifact (lien partageable en dehors de l'app) après rédaction.

## Structure générale de la page

- En-tête fixe : titre "TaskFlow — Documentation" + toggle à deux onglets : **Guide utilisateur** / **Documentation technique**. Changer d'onglet bascule tout le contenu principal (deux jeux de sommaire et sections indépendants, un seul visible à la fois).
- Sidebar de navigation propre à chaque onglet, avec liens ancrés (`#section-id`) vers les sections.
- Barre de recherche en haut de chaque sidebar : filtre en direct (JavaScript vanilla, sans dépendance) les titres de section visibles dans la sidebar et masque les sections non correspondantes dans le contenu.
- Sections organisées en blocs repliables/dépliables (`<details>/<summary>`) pour les sous-thèmes de chaque grande section.
- Thème clair/sombre automatique via `prefers-color-scheme` (media query CSS), pas de toggle manuel nécessaire.
- Design responsive simple (la sidebar passe en menu déroulant/masqué sur petit écran).

## Contenu — Onglet "Guide utilisateur"

Public : utilisateur final de l'application, aucun vocabulaire technique/code.

1. **Démarrage** — créer un compte, se connecter, se déconnecter.
2. **Dashboard** — lecture des statistiques (total, terminées, en cours, en retard, pourcentage complété) et du top 5 des tâches urgentes.
3. **Gérer ses tâches** — créer, éditer, marquer terminée (toggle rapide), supprimer, champs disponibles (titre, description, priorité, échéance, heures estimées).
4. **Filtrer et rechercher** — filtres de statut (en cours/terminée/priorité haute), recherche texte sur le titre, filtre par catégorie, pagination.
5. **Catégories** — créer/éditer/supprimer une catégorie, choix de couleur, catégories par défaut existantes.
6. **Assigner une tâche** à un autre utilisateur de l'application.
7. **Commentaires** — ajouter/supprimer un commentaire sur une tâche.
8. **Pièces jointes** — types de fichiers autorisés, taille maximale (10 Mo), téléchargement, suppression.
9. **FAQ / erreurs courantes** — ex. pourquoi je ne vois pas une tâche qui m'a été assignée (actuellement, `TodoController.Index`/le Dashboard ne filtrent que par `UserId` propriétaire ; l'assignation à un autre utilisateur est une simple étiquette informative, elle ne rend pas la tâche visible dans la liste de la personne assignée — la doc utilisateur doit présenter ce comportement tel quel), messages d'erreur d'upload (fichier trop volumineux, extension non autorisée), etc.

## Contenu — Onglet "Documentation technique"

Public : développeur reprenant ou maintenant le projet. Reprend et enrichit le contenu du `CLAUDE.md` existant, avec extraits de code courts en exemple.

1. **Stack et architecture générale** — ASP.NET Core MVC, ASP.NET Identity, EF Core, SQLite ; schéma du pipeline de démarrage (`Program.cs`).
2. **Modèle de données** — schéma texte des entités (`TodoTask`, `Category`, `Comment`, `Attachment`, `Tag`) et de leurs relations, y compris les champs dénormalisés (`AssignedToUserName`, `UserName` sur `Comment`).
3. **Cycle de vie d'une requête** — routing par convention → contrôleur → vue, pattern PRG.
4. **Pattern de sécurité par propriétaire** — filtrage systématique par `UserId` dans chaque contrôleur, avec extrait de code d'exemple (`TodoController.Edit`).
5. **Upload de fichiers** — contraintes (extensions, taille), stockage sur disque (`wwwroot/uploads`) avec nom généré par GUID.
6. **Pagination** — fonctionnement de `PaginatedList<T>.CreateAsync`.
7. **Commandes de développement** — build, run, migrations EF Core.
8. **Points d'attention connus** — `Tag` non relié à `TodoTask` en base, suppression de catégorie sans vérification préalable des tâches liées (risque d'échec du `DELETE` si contrainte FK stricte).

## Hors périmètre

- Pas de build step (pas de bundler, pas de framework JS) : tout est en HTML/CSS/JS vanilla dans un seul fichier.
- Pas de recherche plein texte dans le corps des sections (uniquement filtre sur les titres de la sidebar), pour rester simple sans dépendance de type Lunr/Fuse.
- Pas de captures d'écran réelles de l'application (pas d'accès à l'app en cours d'exécution pour les générer) — la documentation utilisateur reste textuelle/descriptive, éventuellement avec de petits schémas ASCII/HTML si utile.
- Pas de génération automatique depuis le code (pas de doc-gen type Swagger/DocFX) — contenu rédigé manuellement à partir de l'exploration du code source.
