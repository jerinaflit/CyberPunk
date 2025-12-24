# Mise à Jour : Générateur de Scène Automatique

J'ai ajouté une fonctionnalité majeure à votre outil `ProjectFixer`.

## Nouveau Bouton : "✨ BUILD FULL SCENE (Rue + Hero)"
Ce bouton va automatiquement :
1.  **Créer le décor** : Il va chercher vos images "Rue" (`rue_bg_far`, `rue_bg_mid`, etc.) et les assembler dans la scène.
2.  **Configurer le Parallax** : Il ajoute automatiquement le script `ParallaxLayer` sur chaque couche du décor avec des vitesses différentes pour créer l'effet de profondeur 3D.
3.  **Configurer le Héros** : Il vérifie que le héros a bien son Animator et les paramètres requis (`IsMoving`).

## Comment l'utiliser
1.  Ouvrez le menu **CyberPunk > ✨ Fix & Optimize Project**.
2.  Cliquez sur le bouton **4. ✨ BUILD FULL SCENE**.
3.  Admirez le résultat dans la vue Scene.

## Note sur les Animations
Le script configure le *Contrôleur* d'animation, mais Unity ne permet pas facilement de créer les *États* (les boîtes dans la fenêtre Animator) via script sans risquer de tout casser.
- **Action requise :** Si votre personnage ne bouge pas, ouvrez la fenêtre Animator et vérifiez que vous avez bien glissé vos clips (`Idle`, `Walk`) dedans.

Tout est prêt pour la démo !
