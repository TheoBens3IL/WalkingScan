**Python**
- nbr de pas
- longueur de pas
- Identification des virages
- Vitesse de pas (moyen ? chaque pas ?)
- Asymétrie de marche
- Temps réel

**Unity**
Interface avec choix de :
- distance de parcours total
- nbr de dalles (correspond au nbr de pas)
- distance entre les dalles (correspond à la longueur de chaque pas) <-- Rokoko


Idée pour détetion de pas basé sur l'accéléromètre
- magnitude : $a = \sqrt{x^2 + y^2 + z^2}$
- filtre passe-bas
- détecter pics de la magnitude

___
**Idées perspectives**

- Travailler plutot sur y en fonction de x,z, faire toujours la détection de pics basée sur le changement de signe de la dérivée de - à + , mais rajouter une condition seuil de déplacement selon x,z.

- Générer un dataset en utilisant rokoko, pour détaillé, en plus des données usuelles de positions, les moments où le pied touche le sol (=pas)
- 
- Donner ce dataset à une IA et l'entrainer soit à :
  - On lui donne la méthode d'extraction de pas (variation de la dérivée) et son objectif est de déterminer le seuil idéal pour le filtre (et accessoirement quel filtre) en fonction de la courbe de marche en PosY
  - Extraire lui-même les paramètres de marche sans donner d'indication, simplement en l'entrainant sur le dataset. 

___

**Questions pour Flavie**
- Est-ce que les paramètres longueur et nbr de pas + vitesse suffisent réllement à détecter si une personne va tomber ou non ? Je pencherai plutot sur les données à analyser RotX,Y,Z 
- On rencontre une limite 

___

**Questionnements**
- Pourquoi est-ce que chaque courbe au départ diminue et remonte à la fin sur l'axe y


____
**A faire**
- faire un script d'analyse du taux de bonne réponse en fonction de la BDD correspondant à chaque fichier