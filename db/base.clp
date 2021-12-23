; Этот блок реализует логику обмена информацией с графической оболочкой,
; а также механизм остановки и повторного пуска машины вывода
(deftemplate ioproxy			; шаблон факта-посредника для обмена информацией с GUI
	(slot fact-id)			; теоретически тут id факта для изменения
	(multislot answers)		; возможные ответы
	(multislot messages)		; исходящие сообщения
	(slot reaction)			; возможные ответы пользователя
	(slot value)			; выбор пользователя
	(slot restore)			; забыл зачем это поле
)

; Собственно экземпляр факта ioproxy
(deffacts proxy-fact
	(ioproxy
		(fact-id 0112)		; это поле пока что не задействовано
		(value none)		; значение пустое
		(messages)		; мультислот messages изначально пуст
		(answers)		; мультислот answers тоже
	)
)

(defrule append-answer-and-proceed
	(declare (salience 99))
	?current-answer <- (appendanswer ?new-ans)
	?proxy <- (ioproxy (answers $?ans-list))
	=>
	(printout t "Answer appended : " ?new-ans " ... proceed ... " crlf)
	(modify ?proxy (answers $?ans-list ?new-ans))
	(retract ?current-answer)
)

(defrule clear-messages
	(declare (salience 90))
	?clear-msg-flg <- (clearmessage)
	?proxy <- (ioproxy)
	=>
	(modify ?proxy (messages)(answers))
	(retract ?clear-msg-flg)
	(printout t "Messages cleared ..." crlf)
)

(defrule set-output-and-halt
	(declare (salience 98))
	?current-message <- (sendmessagehalt ?new-msg)
	?proxy <- (ioproxy (messages $?msg-list))
	=>
	(printout t "Message set : " ?new-msg " ... halting ... " crlf)
	(modify ?proxy (messages ?new-msg))
	(retract ?current-message)
	(halt)
)

(defrule append-output-and-halt
	(declare (salience 98))
	?current-message <- (appendmessagehalt $?new-msg)
	?proxy <- (ioproxy (messages $?msg-list))
	=>
	(printout t "Messages appended : " $?new-msg " ... halting ... " crlf)
	(modify ?proxy (messages $?msg-list $?new-msg))
	(retract ?current-message)
	(halt)
)

(defrule set-output-and-proceed
	(declare (salience 98))
	?current-message <- (sendmessage ?new-msg)
	?proxy <- (ioproxy)
	=>
	(printout t "Message set : " ?new-msg " ... proceed ... " crlf)
	(modify ?proxy (messages ?new-msg))
	(retract ?current-message)
)

(defrule append-output-and-proceed
	(declare (salience 98))
	?current-message <- (appendmessage ?new-msg)
	?proxy <- (ioproxy (messages $?msg-list))
	=>
	(printout t "Message appended : " ?new-msg " ... proceed ... " crlf)
	(modify ?proxy (messages $?msg-list ?new-msg))
	(retract ?current-message)
)

;___________________________________________________________________________

(deftemplate fact
	(slot num)
	(slot description)
	(slot certainty)
)

(deffacts start-fact
	(fact (num 6666)(description "Старт"))
)

(defrule welcome
	(declare (salience 100))
	?premise <- (fact (num 6666)(description "Старт"))
	=>
	(retract ?premise)
	(assert (fact (num 5000)(description "Про фичи ещё не спрашивали")))
	(assert (fact (num 5001)(description "Про место ещё не спрашивали")))
	(assert (fact (num 5002)(description "Про компанию ещё не спрашивали")))
	(assert (fact (num 5003)(description "Про алкоголь ещё не спрашивали")))
	(assert (fact (num 5004)(description "Про бюджет ещё не спрашивали")))
	(assert (appendmessagehalt "Здравствуйте, Максим Валентинович!!"))
)

(defrule askforfeatures
	(declare (salience 21))
	?premise <- (fact (num 5000)(description "Про фичи ещё не спрашивали"))
	=>
	(retract ?premise)
	(assert (appendanswer "17-Есть настольный футбол-89-Настольный футбол не обязателен"))
	(assert (appendanswer "22-Можно сидеть после закрытия-97-Нет необходимости сидеть после закрытия"))
	(assert (appendanswer "30-Не требуется QR код-23-Все равно проверяют ли QR кода"))
	(assert (appendanswer "24-Есть кальян-92-Кальян не обязателен"))
	(assert (appendanswer "26-Есть кухня-95-Кухня не обязательна"))
	(assert (appendanswer "28-Милые официанты-98-Устроят обычные официанты"))
	(assert (appendanswer "107-Хочу кота!-108-Кот не обязателен"))
	(assert (appendanswer "110-Есть столики на улице-111-Нет столиков на улице"))
	(assert (appendmessagehalt "#ask_features"))
)


(defrule askforlocation
	(declare (salience 20))
	?premise <- (fact (num 5001)(description ?desc))
	=>
	(retract ?premise)
	(assert (appendanswer "39-Располагается на западном"))
	(assert (appendanswer "40-Располагается на северном"))
	(assert (appendanswer "41-Располагается в центре"))
	(assert (appendmessagehalt "#ask_location"))
)

(defrule askforcompany
	(declare (salience 20))
	?premise <- (fact (num 5002)(description ?desc))
	=>
	(retract ?premise)
	(assert (appendanswer "68-В одиночку"))
	(assert (appendanswer "32-Вдвоем"))
	(assert (appendanswer "66-Компания до 4 человек"))
	(assert (appendanswer "31-Большая компания"))
	(assert (appendmessagehalt "#ask_company"))
)

(defrule askfordrinks
	(declare (salience 20))
	?premise <- (fact (num 5003)(description ?desc))
	=>
	(retract ?premise)
	(assert (appendanswer "1-Коктейли"))
	(assert (appendanswer "2-Крепкие напитки"))
	(assert (appendanswer "3-Пиво"))
	(assert (appendanswer "4-Сидр"))
	(assert (appendanswer "5-Вино"))
	(assert (appendmessagehalt "#ask_drinks"))
)

(defrule askforbudget
	(declare (salience 20))
	?premise <- (fact (num 5004)(description ?desc))
	=>
	(retract ?premise)
	(assert (appendanswer "6-Посидеть на сотку"))
	(assert (appendanswer "7-Посидеть на стипендию"))
	(assert (appendanswer "8-Посидеть на зарплату"))
	(assert (appendmessagehalt "#ask_budget"))
)

(defrule fail
	(declare (salience 10))
	=>
	(assert (appendmessagehalt "Применимые правила закончились :/"))
)

;___________________________________________________________________________
